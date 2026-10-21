class_name ConstructionKitVisual
extends Node3D
## The construction kit (F2-28: blender/build_construction_kit.py; farm-types
## spec §5.3, §6.3 "Construction in 3D") standing on a land campus while it is
## built. It is a presenter: it reads the engine's view of the unit (the
## campus's `view`: the site's project and each lot's own, from
## SimBridge.get_facility_visuals) and nothing else, and never writes the sim.
##
## - Blast pad: while the site works blast the pad (the first 60% of the phase,
##   LandCampusBuilder.BLAST_SHARE) an excavator works beside a blanket of
##   blast mats in the middle of the blasted ground, where the camera frames
##   the campus (nothing else stands during the blast). Both go when it ends.
## - Tank rings: a scaffold ring (Ø29.5 m, 13 m) round each grow-out tank
##   whose walls rise, revealed by the tank's own build progress
##   (INSTANCE_CUSTOM.r: the ring is as tall as the shell). It is struck when
##   the phase ends: its top lift stands in the height band of the tank's
##   walkway, which the walls' top progress (WALLS_TOP_PROGRESS, 12.48 m)
##   has not yet revealed (tests/test_construction_kit_assets.py pins it).
## - Treatment hall: a tower crane at the inland end of each lot whose hall
##   rises, its jib over a first tank and mostly side-on to the land framing
##   (which looks in from the quay edge). Post-smolt modules get none: a
##   50 m crane is out of scale for a 16 m tank.
##
## What is drawn follows the phase keys of the rows alone, so the first-sight
## timelapse (FirstSightDirector.replay_row) plays the kit through the same
## calls. Props are made on first use: a campus that was never under
## construction in this session never creates one. Each stands in the kit's
## own LODs (the crane three, the rest two: spec §5.3's budgets) by visibility
## range, like the campus's building groups; the scaffold rings are one
## MultiMesh per band that follows their tanks' bands.

const DIR := "res://assets/models/construction_kit/"
const ASSETS := ["tower_crane", "scaffold_ring", "excavator", "blast_mats"]
## LODs each asset ships.
const LOD_COUNT := {"tower_crane": 3, "scaffold_ring": 2, "excavator": 2, "blast_mats": 2}
## The crane's base stands this far (m) inland of its lot's end: in the 30 m
## gap between lots (or the pad's 16 m margin), clear of the lot's roads. Its
## slab is 7 m square (the kit's `Base`); it keeps CRANE_EDGE_M off the pad's
## edge, else the lot gets no crane (a short pad turned so that the quay
## zone and a lot fill its depth).
const CRANE_INLAND_M := 8.0
const CRANE_BASE_HALF_M := 3.5
const CRANE_EDGE_M := 2.0
## Its jib's bearing off the lot's axis (rad): toward +v on even slots, -v on
## odd ones, so the first tank of one row stands under the tip.
const CRANE_JIB_RAD := PI / 3.0
## The excavator stands at this (u, v) offset from the campus's focus point
## (the middle of its blasted ground) and turns this far (rad); the blanket
## lies MATS_AHEAD_M in front of it, where its bucket rests on the blanket's
## edge (the preview's arrangement), so the blanket's centre is the focus.
const BLAST_AT := Vector2(-10.0, -7.0)
const BLAST_YAW := 0.9
const MATS_AHEAD_M := 12.0

## The campus this draws for.
var campus: LandCampusBuilder = null

var _cranes := {}                  # lot slot -> Array[MeshInstance3D], one per LOD band
var _blast: Array[MeshInstance3D] = []   # the excavator's and the blanket's bands
var _rings: Array = [null, null]   # band -> MultiMeshInstance3D
var _rings_up := false             # some ring is uploaded (it must be cleared once its phase ends)


## Every kit glb the presenter may load in the background at start.
static func paths() -> PackedStringArray:
	var out := PackedStringArray()
	for asset: String in ASSETS:
		for band in int(LOD_COUNT[asset]):
			var path := "%s%s_%s.glb" % [DIR, asset, LandCampusBuilder.LODS[band]]
			if ResourceLoader.exists(path):
				out.append(path)
	return out


# ── What the engine's phases ask for ──────────────────────────────────────────

## The site's pad is being blasted: its site works under way, short of the blast's share.
static func blasting(build: Dictionary) -> bool:
	return str(build.get("status", "")) == "building" and str(build.get("phase_key", "")) == "site_works" \
		and float(build.get("phase_progress", 0.0)) < LandCampusBuilder.BLAST_SHARE


## A lot's treatment hall is going up.
static func crane_up(build: Dictionary) -> bool:
	return str(build.get("status", "")) == "building" and str(build.get("phase_key", "")) == "treatment_hall"


## A tank's walls are rising (its lot in the tank rings, the engine not yet handed it over).
static func ring_up(build: Dictionary, ready: bool) -> bool:
	return not ready and str(build.get("status", "")) == "building" and str(build.get("phase_key", "")) == "tank_rings"


## Lot `slot`'s own project, else the site's (the row's `build`).
static func lot_build(view: Dictionary, slot: int) -> Dictionary:
	var lots: Array = view.get("lots", [])
	if slot >= 0 and slot < lots.size():
		return (lots[slot] as Dictionary).get("build", view.get("build", {}))
	return view.get("build", {})


# ── Where the props stand (the pad's frame) ───────────────────────────────────

## True when lot `slot`'s crane base stands on the pad: the lot leaves CRANE_INLAND_M
## plus the base and its edge margin between its inland end and the pad's.
static func crane_fits(host: LandCampusBuilder, slot: int) -> bool:
	var inland_end: float = host.lot_uv[slot].x - LandCampusBuilder.LOT_SIZE.x * 0.5
	return inland_end - CRANE_INLAND_M - CRANE_BASE_HALF_M >= -host.half_extents().x + CRANE_EDGE_M


## The crane of lot `slot`: on the lot's axis, CRANE_INLAND_M behind its inland end.
static func crane_xform(host: LandCampusBuilder, slot: int) -> Transform3D:
	var c: Vector2 = host.lot_uv[slot]
	var bearing := CRANE_JIB_RAD if slot % 2 == 0 else -CRANE_JIB_RAD
	# A yaw turns the model's +X (the jib) toward (cos yaw, -sin yaw) in (u, v).
	return host.world_xform(c.x - LandCampusBuilder.LOT_SIZE.x * 0.5 - CRANE_INLAND_M, c.y, 0.0, -bearing)


## The blast's excavator and its blanket of mats at the campus's focus: [excavator, mats].
static func blast_xforms(host: LandCampusBuilder) -> Array[Transform3D]:
	var focus := host.pad_frame().affine_inverse() * host.focus_point()
	var at := Vector2(focus.x, focus.z) + BLAST_AT
	var ahead := at + Vector2(sin(BLAST_YAW), cos(BLAST_YAW)) * MATS_AHEAD_M
	return [host.world_xform(at.x, at.y, 0.0, BLAST_YAW), host.world_xform(ahead.x, ahead.y, 0.0, BLAST_YAW)]


# ── Draw ──────────────────────────────────────────────────────────────────────

func build(host: LandCampusBuilder) -> void:
	name = "ConstructionKit"
	campus = host


## Show the props the campus's view asks for now.
func apply_state() -> void:
	var view := campus.view
	_show_blast(blasting(view.get("build", {})))
	if str(view.get("variant", "")) != "post_smolt":
		for slot in campus.lot_uv.size():
			_show_crane(slot, crane_up(lot_build(view, slot)))
	refresh_rings()


## Re-bucket the scaffold rings with their tanks' bands (the campus calls it
## when its tanks move between bands).
func refresh_rings() -> void:
	var view := campus.view
	# Most syncs find no lot in its tank rings and nothing to clear: no tank loop.
	var wanted := false
	for slot in campus.lot_uv.size():
		wanted = wanted or ring_up(lot_build(view, slot), false)
	if not wanted and not _rings_up:
		return
	_rings_up = false
	for band in 2:
		var xforms: Array[Transform3D] = []
		var customs: Array = []
		for i in campus.tanks.size():
			if campus.tank_band_of(i) != band:
				continue
			var t: Dictionary = campus.tanks[i]
			var progress := campus.tank_progress_of(i)
			if str(t["asset"]) == "tank_growout" and ring_up(lot_build(view, int(t["slot"])), progress >= 1.0):
				xforms.append(t["xform"])
				customs.append(Color(progress, 0.0, 0.0, 0.0))
		if xforms.is_empty() and _rings[band] == null:
			continue
		var mmi := _ring_node(band)
		MultiMeshPack.upload(mmi.multimesh, xforms, [], customs)
		mmi.visible = not xforms.is_empty()
		_rings_up = _rings_up or not xforms.is_empty()


func _show_blast(on: bool) -> void:
	if on and _blast.is_empty():
		var at := blast_xforms(campus)
		_blast = _prop("excavator", "excavator", at[0])
		_blast.append_array(_prop("blast_mats", "blast_mats", at[1]))
	for mi in _blast:
		mi.visible = on


func _show_crane(slot: int, on: bool) -> void:
	if on and not _cranes.has(slot) and crane_fits(campus, slot):
		_cranes[slot] = _prop("crane%d" % slot, "tower_crane", crane_xform(campus, slot))
	if _cranes.has(slot):
		for mi: MeshInstance3D in _cranes[slot]:
			mi.visible = on


## One node per LOD band of `asset` at `xform`, hidden, switched by visibility
## range as the campus's building groups are (LOD0 < 500 m, LOD1 < 2,000 m, the
## crane's LOD2 beyond). Only the near band casts a shadow.
func _prop(node_name: String, asset: String, xform: Transform3D) -> Array[MeshInstance3D]:
	var out: Array[MeshInstance3D] = []
	for band in int(LOD_COUNT[asset]):
		var mesh := LandCampusBuilder.kit_mesh(asset, LandCampusBuilder.LODS[band], DIR)
		if mesh == null:
			continue
		var mi := MeshInstance3D.new()
		mi.name = "%s_LOD%d" % [node_name, band]
		mi.mesh = mesh
		mi.material_override = LandCampusBuilder.MATERIAL
		mi.transform = xform
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if band == 0 else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		mi.visibility_range_begin = 0.0 if band == 0 else LandCampusBuilder.UNIT_LOD_M[band - 1]
		mi.visibility_range_end = LandCampusBuilder.UNIT_LOD_M[band] if band < 2 else 0.0
		mi.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED
		mi.visible = false
		add_child(mi)
		out.append(mi)
	return out


## The scaffold rings of one band: a MultiMesh whose instances carry their
## tank's build progress (INSTANCE_CUSTOM.r) and share its 13 m shell height.
func _ring_node(band: int) -> MultiMeshInstance3D:
	if _rings[band] == null:
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_custom_data = true
		mm.mesh = LandCampusBuilder.kit_mesh("scaffold_ring", LandCampusBuilder.LODS[band], DIR)
		var mmi := MultiMeshInstance3D.new()
		mmi.name = "scaffold_ring_LOD%d" % band
		mmi.multimesh = mm
		mmi.material_override = LandCampusBuilder.MATERIAL
		mmi.set_instance_shader_parameter("instance_custom_on", 1.0)
		mmi.set_instance_shader_parameter("build_height_m", LandCampusBuilder.GROWOUT_H)
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if band == 0 else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		mmi.visible = false
		add_child(mmi)
		_rings[band] = mmi
	return _rings[band]
