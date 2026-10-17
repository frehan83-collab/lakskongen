class_name LandCampusBuilder
extends Node3D
## One land site's campus in the 3D fjord (roadmap F2-20; farm-types spec
## §5.1 "Layouts", §5.4, §5.5, §6.3): the land kit (blender/build_land_base.py,
## LK_FacilityKit) laid out on the site's carved pad from the ENGINE's view of
## the unit (SimBridge.get_facility_visuals: its lots or post-smolt modules
## and tanks, its construction phase, whether it feeds).
##
## Layout, in the pad's own frame: u runs from the pad's centre toward its
## quay edge (the edge facing the channel, TerrainGenerator.QuaySides), v
## along that edge. A grow-out (HFS) site fills lot slots back from the quay
## (two rows of grow-out tanks 4 m apart with the process hall between them
## and a pre-grow tank at each row's end, spec §5.1), and gets the site
## pieces: the quay and the dark harvest hall on the quay side, the orange
## pipe bridge from the quay inland over the lot road, the intake basin, the
## silos, sludge station, substation, gensets and LOX tanks at the back, and
## the roads and aprons L1 shows. A post-smolt site sets its modules (two
## Ø16 m tanks under a lean-to bay) side by side along v, with the river
## intake on a pad that has one.
##
## Draws (spec §5 "Instancing", §5.5 rule 4/5, F2-18): one MultiMesh per tank
## type per LOD band for the whole campus (the bands re-bucket as the camera
## moves: tanks LOD0 < 350 m, LOD1 < 1,200 m), the feeders likewise at LOD0
## and LOD1, and every group of buildings merged into one single-surface mesh
## per LOD (visibility ranges: LOD0 < 500 m, LOD1 < 2,000 m). LOD2 casts no
## shadow; of LOD1 only the tanks do.
##
## Construction (spec §6.3; FacilityConstructionPhases in the sim): blast
## (the terrain's pad_state spreads the granite; the manager sets it),
## foundations (concrete rings: the tank walls cut at under a metre, the
## roads), tanks (the walls rise, INSTANCE_CUSTOM.r), hall (the hall bays and
## the harvest hall rise through build_cut_m), intake (the quay, the pipe
## bridge, the basin and the utilities rise), commissioning (all built), then
## operational: the feeders' rotors turn on the tanks that hold fish while the
## site feeds (INSTANCE_CUSTOM.a >= 1).
##
## Picking: one Area3D, a cylinder per tank (shape index = tank index) over a
## thin box over the pad (any other click on the campus): clicked(tank, at).

signal clicked(tank_index: int, at: Vector3)

const KIT_DIR := "res://assets/models/land_kit/"
const MATERIAL := preload("res://assets/models/facility_kit/LK_FacilityKit.tres")
const LODS := ["LOD0", "LOD1", "LOD2"]
## Spec §5.5 rule 4: tanks LOD0 below 350 m and LOD1 below 1,200 m; units
## (buildings) LOD0 below 500 m and LOD1 below 2,000 m.
const TANK_LOD_M := Vector2(350.0, 1200.0)
const UNIT_LOD_M := Vector2(500.0, 2000.0)
## Re-bucket the tanks when the camera has moved this far (m).
const LOD_REFRESH_M := 25.0
const LOD_HYSTERESIS_M := 8.0
## Pad level (spec §4) and the quay deck (spec §5.1 `quay`).
const PAD_TOP_Y := 3.6
const QUAY_DECK_Y := 3.0
## Tank geometry (spec §5.1; the F2-25 kit, scripts/qa/LandCampusLayout.gd).
const GROWOUT_D := 28.0
const GROWOUT_H := 13.0
const PREGROW_D := 16.0
const PREGROW_H := 9.0
## The feeder sits on the lid's apex (shell + the lid's 2.2% rise).
const FEEDER_Y := {"tank_growout": 13.0 + 0.022 * 28.0, "tank_pregrow": 9.0 + 0.022 * 16.0}
## A full lot (spec §5.1): rows 4 m apart tank to tank, 31 m off the lot's
## axis, the hall's 30 x 20 x 10 m bays between them; about 208 x 90 m.
const LOT_TANK_GAP := 4.0
const LOT_ROW_OFFSET := 31.0
const LOT_SIZE := Vector2(208.0, 90.0)
const LOT_GAP := Vector2(30.0, 15.0)
const HALL_BAY := Vector3(30.0, 10.0, 20.0)
## Margins (m) from the pad's back and side edges to the first lot, and the
## strip on the quay side kept for the harvest hall, the bridge and the apron.
const LOT_MARGIN := Vector2(16.0, 15.0)
const QUAY_ZONE_M := 150.0
## The utilities stand this far (m) behind the last lot row in use.
const UTILITY_GAP_M := 26.0
## Post-smolt module (spec §5.1): two Ø16 m tanks 20 m apart under a lean-to
## bay behind them; modules 44 m apart.
const MODULE_TANK_PITCH := 20.0
const MODULE_PITCH := 44.0
## Pipe bridge (spec §5.1): 25 m spans, the deck 10 m over the pad.
const BRIDGE_SPAN := 25.0
const BRIDGE_DECK_ABOVE_PAD := 10.0
const BRIDGE_SPANS := 8
## Roads (asphalt, the atlas's dark fender strip) and aprons (concrete), m.
const ROAD_WIDTH := 8.0
const ROAD_Y := 0.12
## Atlas rows (blender/build_land_base.py STRIPS) of the road surfaces.
const ROW_ASPHALT := 18
const ROW_CONCRETE := 17
const ATLAS_STRIPS := 32.0
## Half the footprint (m, model x and z: the sidecars' bounding boxes) of
## each piece, for the blasted area round the campus.
const FOOTPRINT_M := {
	"harvest_hall": Vector2(62.5, 37.0), "process_hall_bay": Vector2(15.0, 10.0), "quay": Vector2(60.0, 13.0),
	"pipe_bridge_span": Vector2(13.0, 2.5), "intake_basin": Vector2(43.0, 33.0), "river_intake": Vector2(15.5, 4.5),
	"feed_silo_land": Vector2(5.0, 5.0), "sludge_station": Vector2(10.0, 6.0), "substation": Vector2(9.0, 5.0),
	"genset_container": Vector2(6.1, 1.3), "lox_tank": Vector2(1.8, 1.8),
}
## The blast clears this much (m) round the campus's footprint (spec §6.3:
## only the ground a phase builds on is blasted; the rest of the pad stays heath).
const BLAST_MARGIN_M := 22.0
## Group build heights (m above the pad) the rising cut covers.
const GROUP_RISE := {"halls": Vector2(0.0, 16.0), "intake": Vector2(-16.0, 16.0), "utility": Vector2(0.0, 15.0), "roads": Vector2(-1.0, 1.0)}

## The unit this campus draws (a SimBridge.get_facility_visuals row).
var view: Dictionary = {}
## The pad (FjordTerrain.land_pad_slots row: centre, size, quay).
var pad: Dictionary = {}

## Tanks in layout order: {asset, xform, lot}. Index = the engine's tank order.
var tanks: Array = []
## Building groups: name -> {"pieces": [[asset, local xform]], "origin": Vector3, "nodes": [MeshInstance3D x3]}.
var groups: Dictionary = {}

var _tank_mm := {}   # "asset|band" -> MultiMeshInstance3D
var _feeder_mm := {} # band -> MultiMeshInstance3D
var _bands := PackedInt32Array()
var _last_cam := Vector3(INF, INF, INF)
var _tank_progress := 1.0
var _feeding := false
var _stocked := 0
var _area: Area3D = null
var _frame_basis := Basis.IDENTITY
var _frame_origin := Vector3.ZERO
## The local (u, v) rectangle the campus covers (pieces, roads, tanks).
var _footprint := Rect2()
var _has_footprint := false

static var _mesh_cache := {}


## The single mesh of a kit glb (each LOD is one mesh with one surface).
static func kit_mesh(asset: String, lod: String) -> Mesh:
	var key := asset + "_" + lod
	if not _mesh_cache.has(key):
		var scene := load(KIT_DIR + key + ".glb") as PackedScene
		if scene == null:
			push_error("[LandCampusBuilder] missing kit mesh %s" % key)
			return null
		var root := scene.instantiate()
		var mi := root.find_children("*", "MeshInstance3D", true, false)[0] as MeshInstance3D
		_mesh_cache[key] = mi.mesh
		root.free()
	return _mesh_cache[key]


## Build the campus for `unit_view` on `pad_slot`. Call once; later states go
## through apply_state().
func build(unit_view: Dictionary, pad_slot: Dictionary) -> void:
	view = unit_view
	pad = pad_slot
	name = "Campus_%s" % str(view.get("unit_id", "unit")).validate_node_name()
	_make_frame()
	tanks.clear()
	_footprint = Rect2()
	_has_footprint = false
	groups.clear()
	if str(view.get("variant", "")) == "post_smolt":
		_layout_post_smolt()
	else:
		_layout_grow_out()
	_build_tank_multimeshes()
	_build_groups()
	_build_area()
	apply_state(unit_view)


## The pad's frame: origin at its centre on its top, +X toward the quay (u),
## +Z along the quay edge (v).
func _make_frame() -> void:
	var c: Vector3 = pad.get("centre", Vector3.ZERO)
	var q: Vector3 = pad.get("quay", c + Vector3.RIGHT)
	var to_quay := Vector3(q.x - c.x, 0.0, q.z - c.z)
	to_quay = to_quay.normalized() if to_quay.length() > 0.01 else Vector3.RIGHT
	# Rotating +X by phi about +Y gives (cos, 0, -sin): phi turns +X onto the quay.
	var phi := atan2(-to_quay.z, to_quay.x)
	_frame_basis = Basis(Vector3.UP, phi)
	_frame_origin = Vector3(c.x, PAD_TOP_Y, c.z)


## Half the pad along u (toward the quay) and along v.
func _half() -> Vector2:
	var size: Vector2 = pad.get("size", Vector2(640.0, 420.0))
	var c: Vector3 = pad.get("centre", Vector3.ZERO)
	var q: Vector3 = pad.get("quay", c + Vector3.RIGHT)
	var along_x := absf(q.x - c.x) > absf(q.z - c.z)
	return Vector2(size.x * 0.5, size.y * 0.5) if along_x else Vector2(size.y * 0.5, size.x * 0.5)


## World transform of a piece at local (u, v), height y over the pad, local yaw.
func world_xform(u: float, v: float, y: float, yaw: float) -> Transform3D:
	var local := Transform3D(Basis(Vector3.UP, yaw), Vector3(u, y, v))
	return Transform3D(_frame_basis, _frame_origin) * local


# ── Layout ────────────────────────────────────────────────────────────────────

## A grow-out site grows from its quay: the first lot stands next to the
## harvest hall, the next ones further inland, then a second row of lots;
## the utilities, the service road and the parking follow behind the lot
## rows in use, so a pilot site is a compact yard by the quay and a full
## campus fills the pad (spec §5.1, ref L1).
func _layout_grow_out() -> void:
	var h := _half()
	var lots := int(view.get("lots", 1))
	var grow := int(view.get("grow_out_tanks_per_lot", 12))
	var pre := int(view.get("pre_grow_tanks_per_lot", 2))
	var usable_u := 2.0 * h.x - LOT_MARGIN.x - QUAY_ZONE_M
	var cols := maxi(1, int(floor((usable_u + LOT_GAP.x) / (LOT_SIZE.x + LOT_GAP.x))))
	var rows := int(ceil(float(lots) / float(cols)))
	var used_cols := mini(lots, cols)
	for k in lots:
		var col := k % cols
		var row := k / cols
		var u := lot_u(h, col)
		var v := lot_v(h, row)
		_lot(k, u, v, grow, pre)
	var inland := lot_u(h, used_cols - 1) - LOT_SIZE.x * 0.5
	var back := lot_v(h, rows - 1) + LOT_SIZE.y * 0.5
	_site_pieces(h, inland, back)
	_roads(h, rows, inland, back)


## Centre u of lot column `col` (column 0 by the harvest hall).
static func lot_u(h: Vector2, col: int) -> float:
	return h.x - QUAY_ZONE_M - LOT_SIZE.x * 0.5 - col * (LOT_SIZE.x + LOT_GAP.x)


## Centre v of lot row `row` (row 0 at the pad's -v side).
static func lot_v(h: Vector2, row: int) -> float:
	return -h.y + LOT_MARGIN.y + LOT_SIZE.y * 0.5 + row * (LOT_SIZE.y + LOT_GAP.y)


## One lot centred at local (u, v): grow-out tanks fill the first row, then
## the second (a pilot lot's few stand in the first row, spec §5.1); a
## pre-grow tank closes each row on the quay side; the hall bays run between
## the rows.
func _lot(lot: int, u: float, v: float, grow: int, pre: int) -> void:
	var per_row := 6
	var pitch := GROWOUT_D + LOT_TANK_GAP
	var row_len := per_row * GROWOUT_D + (per_row - 1) * LOT_TANK_GAP
	var u0 := u - LOT_SIZE.x * 0.5 + GROWOUT_D * 0.5
	for k in grow:
		var row := k / per_row
		var side := -1.0 if row == 0 else 1.0
		# A tank's pipes leave on its model -Z side: turn them to the hall.
		var yaw := PI if side < 0.0 else 0.0
		tanks.append({"asset": "tank_growout", "xform": world_xform(u0 + pitch * (k % per_row), v + side * LOT_ROW_OFFSET, 0.0, yaw), "lot": lot})
	for k in pre:
		var side := -1.0 if k == 0 else 1.0
		var yaw := PI if side < 0.0 else 0.0
		var up := u - LOT_SIZE.x * 0.5 + row_len + LOT_TANK_GAP + PREGROW_D * 0.5
		tanks.append({"asset": "tank_pregrow", "xform": world_xform(up, v + side * LOT_ROW_OFFSET, 0.0, yaw), "lot": lot})
	_grow_footprint(Vector2(u, v) - LOT_SIZE * 0.5, 0.0)
	_grow_footprint(Vector2(u, v) + LOT_SIZE * 0.5, 0.0)
	var bays := maxi(1, int(ceil(grow / 2.0)))
	var hu0 := u - LOT_SIZE.x * 0.5 + (row_len - bays * HALL_BAY.x) * 0.5 + HALL_BAY.x * 0.5
	for k in bays:
		_piece("halls", "process_hall_bay", hu0 + HALL_BAY.x * k, v, 0.0, 0.0)


## The grow-out site's own pieces: the quay, the dark harvest hall and the
## orange pipe bridge on the quay side, the intake basin beside the quay,
## and the utilities in a row behind the lot rows in use (from `inland` to
## the hall).
func _site_pieces(h: Vector2, inland: float, back: float) -> void:
	_piece("halls", "harvest_hall", h.x - 90.0, -30.0, 0.0, PI * 0.5)
	_piece("intake", "quay", h.x + 12.5, -40.0, QUAY_DECK_Y - PAD_TOP_Y, PI * 0.5)
	# The orange bridge from the quay inland over the first lot road; a span
	# carries its leg frame at its model -X end, faced away from the quay.
	var bridge_v := lot_v(h, 0) + LOT_SIZE.y * 0.5 + LOT_GAP.y * 0.5
	for k in BRIDGE_SPANS:
		_piece("intake", "pipe_bridge_span", h.x + 10.0 - BRIDGE_SPAN * (k + 0.5), bridge_v, BRIDGE_DECK_ABOVE_PAD, 0.0)
	# The intake basin on the quay edge beside the first lot row.
	_piece("intake", "intake_basin", h.x - 35.0, lot_v(h, 0), 0.0, PI * 0.5)
	# The utilities in one row behind the lots in use, from the substation
	# at the inland end to the feed silos by the cross road.
	var row_v := back + UTILITY_GAP_M
	_piece("utility", "substation", inland + 20.0, row_v, 0.0, 0.0)
	for k in 3:
		_piece("utility", "genset_container", inland + 50.0 + 16.0 * k, row_v + 4.0, 0.0, 0.0)
	for k in 3:
		_piece("utility", "lox_tank", inland + 100.0 + 6.0 * k, row_v, 0.0, 0.0)
	_piece("utility", "sludge_station", inland + 140.0, row_v, 0.0, 0.0)
	_piece("utility", "feed_silo_land", inland + 175.0, row_v, 0.0, 0.0)


## Roads (asphalt) and aprons (concrete) of a grow-out campus (ref L1): a
## lot road between lot rows (the bridge crosses the first), a service road
## between the lots and the utilities, a cross road from it to the pad's -v
## edge between the lots and the hall, the apron on the hall's quay side, and
## parking by the substation.
func _roads(h: Vector2, rows: int, inland: float, back: float) -> void:
	var cross_u := h.x - QUAY_ZONE_M + 15.0
	for r in rows - 1:
		var v := lot_v(h, r) + LOT_SIZE.y * 0.5 + LOT_GAP.y * 0.5
		_rect("roads", ROW_ASPHALT, Vector2(inland - 6.0, v - ROAD_WIDTH * 0.5), Vector2(cross_u, v + ROAD_WIDTH * 0.5))
	var service := back + 10.0
	_rect("roads", ROW_ASPHALT, Vector2(inland - 6.0, service - ROAD_WIDTH * 0.5), Vector2(h.x - 64.0, service + ROAD_WIDTH * 0.5))
	_rect("roads", ROW_ASPHALT, Vector2(cross_u - ROAD_WIDTH * 0.5, -h.y + 8.0), Vector2(cross_u + ROAD_WIDTH * 0.5, service))
	_rect("roads", ROW_CONCRETE, Vector2(h.x - 64.0, -100.0), Vector2(h.x - 1.0, 24.0))
	var row_v := back + UTILITY_GAP_M
	_rect("roads", ROW_CONCRETE, Vector2(inland + 2.0, row_v + 16.0), Vector2(inland + 38.0, row_v + 32.0))


## A post-smolt site: its modules side by side along v, the river intake on
## a pad that has one, the utilities behind.
func _layout_post_smolt() -> void:
	var h := _half()
	var modules := int(view.get("modules", 1))
	var per_module := int(view.get("tanks_per_module", 2))
	var v0 := -float(modules - 1) * MODULE_PITCH * 0.5 - MODULE_TANK_PITCH * 0.5
	for m in modules:
		var v := v0 + MODULE_PITCH * m
		for k in per_module:
			# Pipes (model -Z) toward the bay behind the tanks (-u).
			tanks.append({"asset": "tank_pregrow", "xform": world_xform(20.0, v + MODULE_TANK_PITCH * k, 0.0, PI * 0.5), "lot": m})
			_grow_footprint(Vector2(20.0, v + MODULE_TANK_PITCH * k), PREGROW_D)
		_piece("halls", "process_hall_bay", -2.0, v + MODULE_TANK_PITCH * 0.5, 0.0, -PI * 0.5)
	if bool(view.get("has_river_intake", false)):
		_piece("intake", "river_intake", -h.x + 20.0, -60.0, 0.0, -PI * 0.5)
	_piece("utility", "lox_tank", -h.x + 30.0, 80.0, 0.0, 0.0)
	_piece("utility", "genset_container", -h.x + 24.0, 40.0, 0.0, -PI * 0.5)
	_piece("utility", "substation", h.x - 20.0, h.y - 30.0, 0.0, 0.0)
	_rect("roads", ROW_CONCRETE, Vector2(-30.0, -h.y + 10.0), Vector2(-14.0, h.y - 10.0))


func _piece(group: String, asset: String, u: float, v: float, y: float, yaw: float) -> void:
	if not groups.has(group):
		groups[group] = {"pieces": [], "rects": [], "nodes": []}
	var xf := world_xform(u, v, y, yaw)
	if asset == "pipe_bridge_span":
		xf.origin.y = _frame_origin.y + y
	groups[group]["pieces"].append([asset, xf])
	var half: Vector2 = FOOTPRINT_M.get(asset, Vector2(10.0, 10.0))
	# A quarter turn swaps the model's x and z on the pad.
	if absf(sin(yaw)) > 0.7:
		half = Vector2(half.y, half.x)
	_grow_footprint(Vector2(u, v) - half, 0.0)
	_grow_footprint(Vector2(u, v) + half, 0.0)


func _rect(group: String, row: int, lo: Vector2, hi: Vector2) -> void:
	if not groups.has(group):
		groups[group] = {"pieces": [], "rects": [], "nodes": []}
	groups[group]["rects"].append([row, lo, hi])
	_grow_footprint(lo, 0.0)
	_grow_footprint(hi, 0.0)


## The local (u, v) rectangle the campus covers, grown by `radius` round each point.
func _grow_footprint(at: Vector2, radius: float) -> void:
	var r := Rect2(at - Vector2.ONE * radius, Vector2.ONE * 2.0 * radius)
	_footprint = _footprint.merge(r) if _has_footprint else r
	_has_footprint = true


## The world rectangle (x0, z0, x1, z1) the blast clears for this campus: its
## footprint grown by BLAST_MARGIN_M, out to the quay edge, inside the pad.
func blast_rect() -> Vector4:
	var h := _half()
	var f := _footprint.grow(BLAST_MARGIN_M)
	f = f.merge(Rect2(Vector2(h.x - 1.0, f.position.y), Vector2(1.0, f.size.y)))
	var lo := Vector2(maxf(f.position.x, -h.x), maxf(f.position.y, -h.y))
	var hi := Vector2(minf(f.end.x, h.x), minf(f.end.y, h.y))
	var a := world_xform(lo.x, lo.y, 0.0, 0.0).origin
	var b := world_xform(hi.x, hi.y, 0.0, 0.0).origin
	return Vector4(minf(a.x, b.x), minf(a.z, b.z), maxf(a.x, b.x), maxf(a.z, b.z))


# ── Draws ─────────────────────────────────────────────────────────────────────

func _build_tank_multimeshes() -> void:
	for asset in ["tank_growout", "tank_pregrow"]:
		for band in 3:
			var mmi := _new_mmi("%s_LOD%d" % [asset, band], kit_mesh(asset, LODS[band]))
			mmi.set_instance_shader_parameter("build_height_m", GROWOUT_H if asset == "tank_growout" else PREGROW_H)
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if band < 2 else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			_tank_mm["%s|%d" % [asset, band]] = mmi
	for band in 2:
		var mmi := _new_mmi("tank_feeder_LOD%d" % band, kit_mesh("tank_feeder", LODS[band]))
		# Small and high on the lid: only the near band's shadow reads.
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if band == 0 else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_feeder_mm[band] = mmi
	_bands.resize(tanks.size())
	_bands.fill(-1)


func _new_mmi(node_name: String, mesh: Mesh) -> MultiMeshInstance3D:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = mesh
	var mmi := MultiMeshInstance3D.new()
	mmi.name = node_name
	mmi.multimesh = mm
	mmi.material_override = MATERIAL
	# This node's instances carry build progress, lid, band and rotor.
	mmi.set_instance_shader_parameter("instance_custom_on", 1.0)
	mmi.visible = false
	add_child(mmi)
	return mmi


## Every building group merged per LOD into one single-surface mesh at the
## group's centroid, switched by visibility range.
func _build_groups() -> void:
	for group_name: String in groups:
		var group: Dictionary = groups[group_name]
		var pieces: Array = group["pieces"]
		var rects: Array = group["rects"]
		if pieces.is_empty() and rects.is_empty():
			continue
		var centroid := Vector3.ZERO
		var n := 0
		for p in pieces:
			centroid += (p[1] as Transform3D).origin
			n += 1
		for r in rects:
			var mid: Vector2 = ((r[1] as Vector2) + (r[2] as Vector2)) * 0.5
			centroid += world_xform(mid.x, mid.y, 0.0, 0.0).origin
			n += 1
		centroid /= float(maxi(n, 1))
		centroid.y = _frame_origin.y
		group["origin"] = centroid
		var nodes: Array = []
		for band in 3:
			var st := SurfaceTool.new()
			st.begin(Mesh.PRIMITIVE_TRIANGLES)
			var to_local := Transform3D(Basis.IDENTITY, -centroid)
			var any := false
			for p in pieces:
				var mesh := kit_mesh(str(p[0]), LODS[band])
				if mesh != null:
					st.append_from(mesh, 0, to_local * (p[1] as Transform3D))
					any = true
			if band < 2:
				for r in rects:
					_append_rect(st, to_local, int(r[0]), r[1], r[2])
					any = true
			if not any:
				continue
			var mi := MeshInstance3D.new()
			mi.name = "%s_LOD%d" % [group_name, band]
			mi.mesh = st.commit()
			mi.material_override = MATERIAL
			mi.position = centroid
			mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if band == 0 and group_name != "roads" \
				else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			var begin: float = 0.0 if band == 0 else UNIT_LOD_M[band - 1]
			var end: float = UNIT_LOD_M[band] if band < 2 else 0.0
			mi.visibility_range_begin = begin
			mi.visibility_range_end = end
			mi.visibility_range_begin_margin = LOD_HYSTERESIS_M if begin > 0.0 else 0.0
			mi.visibility_range_end_margin = LOD_HYSTERESIS_M if end > 0.0 else 0.0
			add_child(mi)
			nodes.append(mi)
		group["nodes"] = nodes


## A flat quad (road or apron) ROAD_Y over the pad, its UVs on one atlas row
## (U every 16 m along its length, as the kit's strips repeat).
func _append_rect(st: SurfaceTool, to_local: Transform3D, row: int, lo: Vector2, hi: Vector2) -> void:
	var v_uv := (float(row) + 0.5) / ATLAS_STRIPS
	var corners := [Vector2(lo.x, lo.y), Vector2(hi.x, lo.y), Vector2(hi.x, hi.y), Vector2(lo.x, hi.y)]
	var long_u := (hi.x - lo.x) >= (hi.y - lo.y)
	var pts: Array[Vector3] = []
	var uvs: Array[Vector2] = []
	for c: Vector2 in corners:
		pts.append(to_local * world_xform(c.x, c.y, ROAD_Y, 0.0).origin)
		uvs.append(Vector2((c.x if long_u else c.y) / 16.0, v_uv))
	# Two triangles, clockwise seen from above (Godot's front faces).
	for idx in [0, 1, 2, 0, 2, 3]:
		st.set_normal(Vector3.UP)
		st.set_color(Color(1, 1, 1, 1))
		st.set_uv(uvs[idx])
		st.add_vertex(pts[idx])


## A click target: a cylinder per tank (shape index = tank index) and a thin
## box over the pad for the rest of the campus.
func _build_area() -> void:
	_area = Area3D.new()
	_area.name = "CampusClickArea"
	_area.input_ray_pickable = true
	add_child(_area)
	for t in tanks:
		var shape := CollisionShape3D.new()
		var cyl := CylinderShape3D.new()
		var big := str(t["asset"]) == "tank_growout"
		cyl.radius = (GROWOUT_D if big else PREGROW_D) * 0.5 + 1.0
		cyl.height = GROWOUT_H if big else PREGROW_H
		shape.shape = cyl
		var xf: Transform3D = t["xform"]
		shape.position = xf.origin + Vector3(0.0, cyl.height * 0.5, 0.0)
		_area.add_child(shape)
	var floor_shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	var h := _half()
	box.size = Vector3(2.0 * h.x, 1.0, 2.0 * h.y)
	floor_shape.shape = box
	floor_shape.transform = Transform3D(_frame_basis, _frame_origin + Vector3(0.0, 0.5, 0.0))
	_area.add_child(floor_shape)
	_area.input_event.connect(_on_input_event)


func _on_input_event(_camera: Node, event: InputEvent, at: Vector3, _normal: Vector3, shape_idx: int) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		clicked.emit(shape_idx if shape_idx < tanks.size() else -1, at)


# ── State ─────────────────────────────────────────────────────────────────────

## The unit's construction phase and feeding (spec §6.3).
func apply_state(unit_view: Dictionary) -> void:
	view = unit_view
	var phase := int(view.get("phase_index", 0))
	var p := clampf(float(view.get("phase_progress", 0.0)), 0.0, 1.0)
	var operational := bool(view.get("operational", false))
	if operational:
		phase = int(view.get("phase_count", 6))
	# Tank walls: hidden while the pad is blasted, concrete rings under the
	# foundations, rising through the tanks phase, whole after.
	if phase <= 0:
		_tank_progress = 0.0
	elif phase == 1:
		_tank_progress = 0.02 + 0.05 * p
	elif phase == 2:
		_tank_progress = 0.07 + 0.93 * p
	else:
		_tank_progress = 1.0
	_set_group("roads", 0.0 if phase < 1 else 1.0)
	_set_group("halls", 0.0 if phase < 3 else (p if phase == 3 else 1.0))
	_set_group("intake", 0.0 if phase < 4 else (p if phase == 4 else 1.0))
	_set_group("utility", 0.0 if phase < 4 else (p if phase == 4 else 1.0))
	_feeding = operational and bool(view.get("feeding", false))
	_stocked = int(view.get("stocked_tanks", 0))
	_last_cam = Vector3(INF, INF, INF)
	_bands.fill(-1)
	refresh_lods(_camera_position(), true)


## A group shown to `share` of its rise (0 hidden, 1 whole).
func _set_group(group_name: String, share: float) -> void:
	if not groups.has(group_name):
		return
	var rise: Vector2 = GROUP_RISE.get(group_name, Vector2(0.0, 16.0))
	for mi: MeshInstance3D in groups[group_name].get("nodes", []):
		mi.visible = share > 0.0
		mi.set_instance_shader_parameter("build_cut_m", lerpf(rise.x, rise.y, share) if share < 1.0 else 100000.0)


## The build progress the tank walls stand at (0-1).
func get_tank_progress() -> float:
	return _tank_progress


## Re-bucket the tanks into their LOD bands for a camera at `cam_pos` and
## upload the MultiMeshes whose members changed.
func refresh_lods(cam_pos: Vector3, force: bool = false) -> void:
	if not force and cam_pos.distance_to(_last_cam) < LOD_REFRESH_M:
		return
	_last_cam = cam_pos
	var changed := force
	for i in tanks.size():
		var d := cam_pos.distance_to((tanks[i]["xform"] as Transform3D).origin)
		var band := 0 if d < TANK_LOD_M.x else (1 if d < TANK_LOD_M.y else 2)
		if band != _bands[i]:
			_bands[i] = band
			changed = true
	if changed:
		_upload()


func _upload() -> void:
	var shown := _tank_progress > 0.0
	for asset in ["tank_growout", "tank_pregrow"]:
		for band in 3:
			var xforms: Array[Transform3D] = []
			var customs: Array = []
			for i in tanks.size():
				if tanks[i]["asset"] == asset and _bands[i] == band:
					xforms.append(tanks[i]["xform"])
					customs.append(Color(_tank_progress, 0.0, 0.0, 0.0))
			var mmi: MultiMeshInstance3D = _tank_mm["%s|%d" % [asset, band]]
			MultiMeshPack.upload(mmi.multimesh, xforms, [], customs)
			mmi.visible = shown and not xforms.is_empty()
	var built := _tank_progress >= 1.0
	for band in 2:
		var xforms: Array[Transform3D] = []
		var customs: Array = []
		for i in tanks.size():
			if _bands[i] != band:
				continue
			var xf: Transform3D = tanks[i]["xform"]
			xforms.append(Transform3D(xf.basis, xf.origin + Vector3(0.0, FEEDER_Y[tanks[i]["asset"]], 0.0)))
			# Phase from the tank's place (rotors out of step); >= 1 turns.
			var phase := fmod(float(i) * 0.37, 1.0)
			var spins := _feeding and i < _stocked
			customs.append(Color(1.0, 0.0, 0.0, phase + (1.0 if spins else 0.0)))
		var mmi: MultiMeshInstance3D = _feeder_mm[band]
		MultiMeshPack.upload(mmi.multimesh, xforms, [], customs)
		mmi.visible = built and not xforms.is_empty()


## Tanks drawn now per asset (every band): the presenter's counts.
func drawn_tanks(asset: String) -> int:
	var n := 0
	for key: String in _tank_mm:
		if key.begins_with(asset + "|"):
			var mmi: MultiMeshInstance3D = _tank_mm[key]
			if mmi.visible:
				n += mmi.multimesh.instance_count
	return n


## Feeders turning now.
func spinning_feeders() -> int:
	var n := 0
	for band in 2:
		var mmi: MultiMeshInstance3D = _feeder_mm[band]
		if not mmi.visible:
			continue
		var mm := mmi.multimesh
		for k in mm.instance_count:
			if mm.get_instance_custom_data(k).a >= 1.0:
				n += 1
	return n


## Triangles the campus draws when all of it stands in the LOD0 band (spec
## §5 budget: a full campus <= 250 k inside 600 m): every tank and feeder
## at LOD0 plus the merged LOD0 groups.
func lod0_triangles() -> int:
	var n := 0
	for t in tanks:
		n += mesh_triangles(kit_mesh(str(t["asset"]), LODS[0])) + mesh_triangles(kit_mesh("tank_feeder", LODS[0]))
	for group_name: String in groups:
		for mi: MeshInstance3D in groups[group_name].get("nodes", []):
			if mi.name.ends_with("_LOD0"):
				n += mesh_triangles(mi.mesh)
	return n


static func mesh_triangles(mesh: Mesh) -> int:
	if mesh == null:
		return 0
	var n := 0
	for s in mesh.get_surface_count():
		var arrays := mesh.surface_get_arrays(s)
		var idx: PackedInt32Array = arrays[Mesh.ARRAY_INDEX] if arrays[Mesh.ARRAY_INDEX] != null else PackedInt32Array()
		n += (idx.size() if not idx.is_empty() else (arrays[Mesh.ARRAY_VERTEX] as PackedVector3Array).size()) / 3
	return n


## Every draw node with its LOD band and whether it casts a shadow (spec §5:
## LOD2 casts none; of LOD1 only the tanks do): [{name, band, kind, casts}].
func shadow_report() -> Array:
	var out: Array = []
	for key: String in _tank_mm:
		var mmi: MultiMeshInstance3D = _tank_mm[key]
		out.append({"name": String(mmi.name), "band": int(key.get_slice("|", 1)), "kind": "tank",
			"casts": mmi.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF})
	for band: int in _feeder_mm:
		var f: MultiMeshInstance3D = _feeder_mm[band]
		out.append({"name": String(f.name), "band": band, "kind": "feeder",
			"casts": f.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF})
	for group_name: String in groups:
		for mi: MeshInstance3D in groups[group_name].get("nodes", []):
			out.append({"name": String(mi.name), "band": int(String(mi.name).get_slice("_LOD", 1)), "kind": "building",
				"casts": mi.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF})
	return out


## World position of tank `index` (its base centre), or the pad's centre.
func tank_position(index: int) -> Vector3:
	if index >= 0 and index < tanks.size():
		return (tanks[index]["xform"] as Transform3D).origin
	return _frame_origin


## The lot (or module) of tank `index` (-1 for the campus).
func tank_lot(index: int) -> int:
	return int(tanks[index]["lot"]) if index >= 0 and index < tanks.size() else -1


## Centre of lot (or module) `lot`'s tanks.
func lot_centre(lot: int) -> Vector3:
	var sum := Vector3.ZERO
	var n := 0
	for t in tanks:
		if int(t["lot"]) == lot:
			sum += (t["xform"] as Transform3D).origin
			n += 1
	return sum / float(n) if n > 0 else _frame_origin


## The campus's centre on the pad.
func centre() -> Vector3:
	return _frame_origin


func _process(_delta: float) -> void:
	refresh_lods(_camera_position())


func _camera_position() -> Vector3:
	if not is_inside_tree():
		return Vector3(INF, INF, INF)
	var cam := get_viewport().get_camera_3d()
	return cam.global_position if cam != null else Vector3(INF, INF, INF)


## Where the camera frames the campus: the middle of its blasted ground.
func focus_point() -> Vector3:
	var r := blast_rect()
	return Vector3((r.x + r.z) * 0.5, PAD_TOP_Y, (r.y + r.w) * 0.5)
