class_name GrayBoxBuilder
extends RefCounted
## Primitive stand-ins for the farm types (F2-18 gray-box read gate): the
## land campus on every pad (LandCampusLayout), the 110 m OF1-class ring at
## off_1 and the 385 m Havfarm-class hull at off_2. Sizes and colours are
## the farm-types spec's (§5.1, §5.2), so the read gate measures what the
## kit will show before a triangle of the kit is spent.
##
## The draw structure copies F2-20's plan (spec §5): one MultiMesh per tank
## type for the whole plot, every static piece of a plot merged into one
## vertex-coloured mesh, one mesh per offshore unit. A "sample" grow-out
## tank per pad is left out of the MultiMesh and drawn as a wall and a lid
## of its own, so the mask pass can measure one tank and its lid ellipse.
##
## Every drawn node carries meta "graybox_class" (a CLASS_* name below) for
## the mask pass in FarmTypeGrayBox.gd.

const LAYOUT := preload("res://scripts/qa/LandCampusLayout.gd")

## Spec swatches (sRGB).
const C_WALL := Color("#C9CED1")
const C_BAND := Color("#5E7A6A")
const C_LID := Color("#2A3036")
const C_HALL := Color("#D5D9DB")
const C_ROOF := Color("#4A5056")
const C_HARVEST := Color("#3A4046")
const C_ORANGE := Color("#D9622B")
const C_PIPE := Color("#9AA0A6")
const C_CONCRETE := Color("#9EA3A6")
const C_LOX := Color("#E9ECEA")
const C_GENSET := Color("#4A5650")
const C_SILO := Color("#8A8F96")
const C_SLUDGE := Color("#6E7B73")
const C_GRANITE := Color("#6B7378")
const C_HULL_YELLOW := Color("#E6BE2E")
const C_TOPSIDES := Color("#E9ECEA")
const C_HAVFARM_HULL := Color("#34424B")

## Tank wall band (spec §5.1: lower 4 m, ref L2).
const BAND_H := 4.0
const TANK_SEGMENTS := 32

static var _material: StandardMaterial3D = null


## Build everything under `parent`. `samples` maps a pad id to the index of
## its sample grow-out tank (in LandCampusLayout order); pads not in it get
## none. Returns the root node.
static func build(parent: Node3D, samples: Dictionary = {}) -> Node3D:
	var root := Node3D.new()
	root.name = "FarmTypeGrayBox"
	parent.add_child(root)
	for plot_id in ["pad_a", "pad_b", "pad_delta"]:
		_build_pad(root, plot_id, int(samples.get(plot_id, -1)))
	_build_offshore(root, "off_1")
	_build_offshore(root, "off_2")
	return root


## Grow-out tank placements of a pad, in layout order.
static func growout_positions(plot_id: String) -> Array[Vector3]:
	var out: Array[Vector3] = []
	for p in LAYOUT.plot_placements(plot_id):
		if p["asset"] == "tank_growout":
			out.append(p["pos"])
	return out


# ── Land ─────────────────────────────────────────────────────────────────────

static func _build_pad(root: Node3D, plot_id: String, sample: int) -> void:
	var plot: Dictionary = LAYOUT.PLOTS[plot_id]
	var node := Node3D.new()
	node.name = plot_id
	root.add_child(node)
	var centre := LAYOUT.plot_centre(plot_id)
	var size: Vector2 = plot["size"]
	# The blasted pad (F2-19 carves it into the terrain; here it is a slab).
	var slab := SurfaceTool.new()
	slab.begin(Mesh.PRIMITIVE_TRIANGLES)
	_box(slab, Transform3D(Basis(), Vector3(centre.x, -13.2, centre.z)), Vector3(size.x, 33.6, size.y), C_GRANITE)
	_add_mesh(node, "Pad", slab, "%s_pad" % plot_id)
	var cls := "%s_campus" % plot_id
	var growout: Array[Transform3D] = []
	var pregrow: Array[Transform3D] = []
	var statics := SurfaceTool.new()
	statics.begin(Mesh.PRIMITIVE_TRIANGLES)
	var g_index := 0
	for p in LAYOUT.plot_placements(plot_id):
		var xf := Transform3D(Basis(Vector3.UP, float(p["yaw"])), p["pos"])
		match p["asset"]:
			"tank_growout":
				if g_index == sample:
					_sample_tank(node, plot_id, xf)
				else:
					growout.append(xf)
				g_index += 1
			"tank_pregrow":
				pregrow.append(xf)
			_:
				_static_piece(statics, p, xf)
	_add_multimesh(node, "Growout", _tank_mesh(LAYOUT.GROWOUT_D, LAYOUT.GROWOUT_H), growout, cls)
	_add_multimesh(node, "Pregrow", _tank_mesh(LAYOUT.PREGROW_D, LAYOUT.PREGROW_H), pregrow, cls)
	_add_mesh(node, "Static", statics, cls)


static func _sample_tank(node: Node3D, plot_id: String, xf: Transform3D) -> void:
	var r := LAYOUT.GROWOUT_D * 0.5
	var wall := SurfaceTool.new()
	wall.begin(Mesh.PRIMITIVE_TRIANGLES)
	_tank_wall(wall, xf, r, LAYOUT.GROWOUT_H)
	_add_mesh(node, "SampleWall", wall, "%s_tank_wall" % plot_id)
	var lid := SurfaceTool.new()
	lid.begin(Mesh.PRIMITIVE_TRIANGLES)
	_disc(lid, xf, r, LAYOUT.GROWOUT_H + 0.05, C_LID)
	_add_mesh(node, "SampleLid", lid, "%s_tank_lid" % plot_id)


static func _tank_mesh(d: float, h: float) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	_tank_wall(st, Transform3D(), d * 0.5, h)
	_disc(st, Transform3D(), d * 0.5, h + 0.05, C_LID)
	st.generate_normals()
	return st.commit()


static func _tank_wall(st: SurfaceTool, xf: Transform3D, r: float, h: float) -> void:
	_cylinder(st, xf, r, 0.0, BAND_H, C_BAND, false)
	_cylinder(st, xf, r, BAND_H, h, C_WALL, false)


## Boxes and cylinders for every static kit piece (sizes from the layout).
static func _static_piece(st: SurfaceTool, p: Dictionary, xf: Transform3D) -> void:
	var s: Vector3 = p["size"]
	match p["asset"]:
		"process_hall_bay":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5 - 0.3, 0)), Vector3(s.x, s.y - 0.6, s.z), C_HALL)
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y - 0.3, 0)), Vector3(s.x, 0.6, s.z), C_ROOF)
		"harvest_hall":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5, 0)), s, C_HARVEST)
		"pipe_bridge_span":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y + 1.75, 0)), Vector3(s.x, 3.5, s.z), C_ORANGE)
			_box(st, xf * Transform3D(Basis(), Vector3(-s.x * 0.5 + 0.6, s.y * 0.5, 0)), Vector3(1.0, s.y, 2.5), C_ORANGE)
		"quay":
			_box(st, xf * Transform3D(Basis(), Vector3(0, -6.0 + s.y * 0.5 - 1.5, 0)), Vector3(s.x, 12.0, s.z), C_CONCRETE)
		"intake_basin":
			var arc_r := 45.0
			for k in 14:
				var a := deg_to_rad(-70.0 + 140.0 * (k + 0.5) / 14.0)
				var seg := Transform3D(Basis(Vector3.UP, a), Vector3(sin(a) * arc_r, 0, cos(a) * arc_r))
				_box(st, xf * seg * Transform3D(Basis(), Vector3(0, -4.0, 0)), Vector3(11.4, 14.0, 2.0), C_CONCRETE)
			_box(st, xf * Transform3D(Basis(), Vector3(0, 4.5, -10.0)), Vector3(22.0, 9.0, 12.0), C_HALL)
		"river_intake":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5, 0)), s, C_CONCRETE)
		"lox_tank":
			_cylinder(st, xf, s.x * 0.5, 0.0, s.y, C_LOX, true)
		"genset_container":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5, 0)), s, C_GENSET)
		"substation":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5, 0)), s, C_ORANGE)
		"feed_silo_land":
			for k in 4:
				var o := Vector3((k % 2) * 5.0 - 2.5, 0, (k / 2) * 5.0 - 2.5)
				_cylinder(st, xf * Transform3D(Basis(), o), 2.0, 0.0, s.y, C_SILO, true)
		"sludge_station":
			_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5, 0)), s, C_SLUDGE)


# ── Offshore ─────────────────────────────────────────────────────────────────

static func _build_offshore(root: Node3D, plot_id: String) -> void:
	var p: Dictionary = LAYOUT.plot_placements(plot_id)[0]
	var node := Node3D.new()
	node.name = plot_id
	root.add_child(node)
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var xf := Transform3D(Basis(), p["pos"])
	if p["asset"] == "of1_class":
		_of1(st, xf)
	else:
		_havfarm(st, xf)
	_add_mesh(node, "Unit", st, "%s_unit" % plot_id)


## OF1-class (spec §5.2): 12-gon, circumradius 55; large columns (Ø3.56) on
## cone feet alternating with slim ones (Ø2.8); top ring, a mid ring and a
## zig-zag diagonal in every bay; a gantry across with the topsides block
## and silos at the centre. The ring tops out 18 m over the water and the
## topsides 25 m.
static func _of1(st: SurfaceTool, xf: Transform3D) -> void:
	var r := LAYOUT.OF1_RADIUS
	var top := 18.0
	var mid := 6.0
	var bottom := -6.0
	var pts: Array[Vector3] = []
	for k in 12:
		var a := TAU * k / 12.0
		pts.append(Vector3(sin(a) * r, 0, cos(a) * r))
	for k in 12:
		var big := k % 2 == 0
		var cr := 1.78 if big else 1.4
		_cylinder(st, xf * Transform3D(Basis(), pts[k]), cr, bottom, top, C_HULL_YELLOW, true)
		if big:
			_cylinder(st, xf * Transform3D(Basis(), pts[k]), 2.6, bottom - 4.0, bottom + 0.5, C_HULL_YELLOW, true)
	for k in 12:
		var a0: Vector3 = pts[k]
		var a1: Vector3 = pts[(k + 1) % 12]
		_beam(st, xf, a0 + Vector3(0, top, 0), a1 + Vector3(0, top, 0), 1.15, C_HULL_YELLOW)
		_beam(st, xf, a0 + Vector3(0, mid, 0), a1 + Vector3(0, mid, 0), 0.5, C_HULL_YELLOW)
		var lo := a0 + Vector3(0, mid, 0) if k % 2 == 0 else a1 + Vector3(0, mid, 0)
		var hi := a1 + Vector3(0, top, 0) if k % 2 == 0 else a0 + Vector3(0, top, 0)
		_beam(st, xf, lo, hi, 0.5, C_HULL_YELLOW)
	# Centre column, cross gantry with its walkway, topsides and silos.
	_cylinder(st, xf, 1.78, bottom, top, C_HULL_YELLOW, true)
	_box(st, xf * Transform3D(Basis(), Vector3(0, top, 0)), Vector3(2.0 * r, 2.05, 2.5), C_HULL_YELLOW)
	_box(st, xf * Transform3D(Basis(), Vector3(0, top, 0)), Vector3(2.5, 2.05, 2.0 * r), C_HULL_YELLOW)
	_box(st, xf * Transform3D(Basis(), Vector3(0, top + 3.5, 0)), Vector3(14.0, 7.0, 14.0), C_TOPSIDES)
	for k in 4:
		var o := Vector3((k % 2) * 18.0 - 9.0, top, (k / 2) * 18.0 - 9.0)
		_cylinder(st, xf * Transform3D(Basis(), o), 2.0, 0.0, 10.0 - 3.0, C_SILO, true)


## Havfarm-class (spec §5.2): 385 x 59.5 m hull, 11 m freeboard, a white
## aft superstructure 40 x 18 m; broadside to the home camera (along X).
static func _havfarm(st: SurfaceTool, xf: Transform3D) -> void:
	var s := LAYOUT.HAVFARM_SIZE
	_box(st, xf * Transform3D(Basis(), Vector3(0, s.y * 0.5 - 2.5, 0)), Vector3(s.x, s.y + 5.0, s.z), C_HAVFARM_HULL)
	_box(st, xf * Transform3D(Basis(), Vector3(s.x * 0.5 - 20.0, s.y + 9.0, 0)), Vector3(40.0, 18.0, s.z), C_TOPSIDES)


# ── Mesh helpers ─────────────────────────────────────────────────────────────

static func material() -> StandardMaterial3D:
	if _material == null:
		_material = StandardMaterial3D.new()
		_material.vertex_color_use_as_albedo = true
		_material.vertex_color_is_srgb = true
		_material.roughness = 0.85
	return _material


static func _add_mesh(parent: Node3D, node_name: String, st: SurfaceTool, cls: String) -> MeshInstance3D:
	st.generate_normals()
	var mi := MeshInstance3D.new()
	mi.name = node_name
	mi.mesh = st.commit()
	mi.material_override = material()
	mi.set_meta("graybox_class", cls)
	parent.add_child(mi)
	return mi


static func _add_multimesh(parent: Node3D, node_name: String, mesh: ArrayMesh, xforms: Array[Transform3D], cls: String) -> void:
	if xforms.is_empty():
		return
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = mesh
	mm.instance_count = xforms.size()
	for i in xforms.size():
		mm.set_instance_transform(i, xforms[i])
	var mmi := MultiMeshInstance3D.new()
	mmi.name = node_name
	mmi.multimesh = mm
	mmi.material_override = material()
	mmi.set_meta("graybox_class", cls)
	parent.add_child(mmi)


static func _box(st: SurfaceTool, xf: Transform3D, size: Vector3, color: Color) -> void:
	var h := size * 0.5
	var c := [
		Vector3(-h.x, -h.y, -h.z), Vector3(h.x, -h.y, -h.z), Vector3(h.x, h.y, -h.z), Vector3(-h.x, h.y, -h.z),
		Vector3(-h.x, -h.y, h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, h.y, h.z), Vector3(-h.x, h.y, h.z),
	]
	# Outward faces (counter-clockwise seen from outside, Godot front face).
	var faces := [[4, 5, 6, 7], [1, 0, 3, 2], [0, 4, 7, 3], [5, 1, 2, 6], [7, 6, 2, 3], [0, 1, 5, 4]]
	for f in faces:
		_quad(st, xf * c[f[0]], xf * c[f[1]], xf * c[f[2]], xf * c[f[3]], color)


static func _cylinder(st: SurfaceTool, xf: Transform3D, r: float, y0: float, y1: float, color: Color, cap: bool) -> void:
	for k in TANK_SEGMENTS:
		var a0 := TAU * k / TANK_SEGMENTS
		var a1 := TAU * (k + 1) / TANK_SEGMENTS
		var p0 := Vector3(sin(a0) * r, 0, cos(a0) * r)
		var p1 := Vector3(sin(a1) * r, 0, cos(a1) * r)
		_quad(st, xf * (p0 + Vector3(0, y0, 0)), xf * (p1 + Vector3(0, y0, 0)), xf * (p1 + Vector3(0, y1, 0)), xf * (p0 + Vector3(0, y1, 0)), color)
	if cap:
		_disc(st, xf, r, y1, color)


static func _disc(st: SurfaceTool, xf: Transform3D, r: float, y: float, color: Color) -> void:
	var centre := xf * Vector3(0, y, 0)
	for k in TANK_SEGMENTS:
		var a0 := TAU * k / TANK_SEGMENTS
		var a1 := TAU * (k + 1) / TANK_SEGMENTS
		_tri(st, centre, xf * Vector3(sin(a0) * r, y, cos(a0) * r), xf * Vector3(sin(a1) * r, y, cos(a1) * r), color)


## A square beam of side 2*r between two points (local to xf).
static func _beam(st: SurfaceTool, xf: Transform3D, a: Vector3, b: Vector3, r: float, color: Color) -> void:
	var d := b - a
	var length := d.length()
	if length < 0.01:
		return
	var z := d / length
	var x := z.cross(Vector3.UP)
	if x.length() < 0.01:
		x = z.cross(Vector3.RIGHT)
	x = x.normalized()
	var y := z.cross(x).normalized()
	var basis := Basis(x, y, z)
	_box(st, xf * Transform3D(basis, (a + b) * 0.5), Vector3(2.0 * r, 2.0 * r, length), color)


static func _quad(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, d: Vector3, color: Color) -> void:
	_tri(st, a, b, c, color)
	_tri(st, a, c, d, color)


static func _tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, color: Color) -> void:
	# Godot's front faces are clockwise in screen space: emit a, c, b.
	st.set_color(color)
	st.add_vertex(a)
	st.set_color(color)
	st.add_vertex(c)
	st.set_color(color)
	st.add_vertex(b)
