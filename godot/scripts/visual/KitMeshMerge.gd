class_name KitMeshMerge
extends RefCounted
## Merges kit pieces into one single-surface mesh on the CPU (roadmap F2-20,
## the facility presenter's building groups).
##
## SurfaceTool.append_from reads every appended mesh back from the GPU
## (Mesh.surface_get_arrays: ~0.6 ms a call on Metal), so merging a 4-lot
## campus's groups cost 84 ms a build, 236 ms cold (wave4b review: a
## main-thread stall on every build, expansion, cancel or load). Here each
## kit mesh is read back once per session (arrays_of) and the merge is
## packed-array work: positions by the piece's transform, normals by its
## basis (kit pieces only turn), colours and UVs as they are, indices offset
## per piece. The facility kit's material reads no tangents, so none are kept.

## CPU copies of kit surfaces by key, and how many were read back from the
## GPU this session (the presenter's checks hold it to once per key).
static var _arrays := {}
static var read_backs := 0


## The surface arrays of `mesh` (surface 0), read from the GPU the first time
## `key` is asked for; [] for no mesh.
static func arrays_of(key: String, mesh: Mesh) -> Array:
	if not _arrays.has(key):
		if mesh == null:
			return []
		_arrays[key] = mesh.surface_get_arrays(0)
		read_backs += 1
	return _arrays[key]


## One triangle mesh of `parts` ([surface arrays, Transform3D] each).
static func merge(parts: Array) -> ArrayMesh:
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	for part: Array in parts:
		var a: Array = part[0]
		var xf: Transform3D = part[1]
		var pv: PackedVector3Array = a[Mesh.ARRAY_VERTEX]
		var n := pv.size()
		var base := verts.size()
		verts.append_array(xf * pv)
		normals.append_array(_normals(a, n, xf.basis))
		colors.append_array(_colors(a, n))
		uvs.append_array(_uvs(a, n))
		var start := indices.size()
		var idx = a[Mesh.ARRAY_INDEX]
		if idx is PackedInt32Array and not (idx as PackedInt32Array).is_empty():
			indices.append_array(idx)
		else:
			indices.resize(start + n)
			for i in n:
				indices[start + i] = i
		if base > 0:
			for i in range(start, indices.size()):
				indices[i] += base
	var out := []
	out.resize(Mesh.ARRAY_MAX)
	out[Mesh.ARRAY_VERTEX] = verts
	out[Mesh.ARRAY_NORMAL] = normals
	out[Mesh.ARRAY_COLOR] = colors
	out[Mesh.ARRAY_TEX_UV] = uvs
	out[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, out)
	return mesh


static func _normals(a: Array, n: int, basis: Basis) -> PackedVector3Array:
	var src = a[Mesh.ARRAY_NORMAL]
	if src is PackedVector3Array and (src as PackedVector3Array).size() == n:
		return Transform3D(basis, Vector3.ZERO) * (src as PackedVector3Array)
	var up := PackedVector3Array()
	up.resize(n)
	up.fill(Vector3.UP)
	return up


static func _colors(a: Array, n: int) -> PackedColorArray:
	var src = a[Mesh.ARRAY_COLOR]
	if src is PackedColorArray and (src as PackedColorArray).size() == n:
		return src
	var white := PackedColorArray()
	white.resize(n)
	white.fill(Color.WHITE)
	return white


static func _uvs(a: Array, n: int) -> PackedVector2Array:
	var src = a[Mesh.ARRAY_TEX_UV]
	if src is PackedVector2Array and (src as PackedVector2Array).size() == n:
		return src
	var zero := PackedVector2Array()
	zero.resize(n)
	return zero
