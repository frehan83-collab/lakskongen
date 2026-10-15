extends Node
class_name CompetitorFarmVisualManager

## Competitor farm visual manager: places the AI competitors' farms on the
## fjord channel's deep-water slots, filled from the far end (the player's
## sites fill them from the near end). A rival farm is the very FarmSite the
## player's sites are (roadmap G2-04): a grid of the hero pens in a frame
## mooring, a feed barge moored beside it with white hoses to every pen, in
## the rival's colour band, with orange buoys, quieter nets and no health
## rings. Shows up to max_competitors farms; a rival whose slot the
## player's sites reach is not drawn.

@export var max_competitors: int = 8
@export var feed_barge_scene: PackedScene = preload("res://assets/models/feed_barge/feed_barge.tscn")

signal competitor_farm_added(competitor_id: String)

var _sim_bridge = null
var _fjord_env = null
var _competitor_farms: Dictionary = {}  # competitor_id -> {root, site, cages, label, mooring, barge}

func _ready() -> void:
	_fjord_env = get_parent()
	var sim = get_node_or_null("/root/SimBridge")
	if sim:
		bind_simulation(sim)
	print("[CompetitorFarmVisualManager] Ready")

func bind_simulation(sim_bridge) -> void:
	_sim_bridge = sim_bridge
	if _sim_bridge:
		_sim_bridge.state_updated.connect(_on_state_updated)

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	_sync_competitor_farms()

func _sync_competitor_farms() -> void:
	# GetCompetitorCompanies lives on GameBridge, not SimBridge, so this used to
	# return before drawing anything (review F744).
	if not _sim_bridge:
		return
	var pens := _fjord_env.get_node_or_null("FishPenManager") as FishPenManager
	if pens == null:
		return
	# The bridge's own farm positions (x up to ±1000 m) lie inside the fjord
	# walls, so the farms take the pen manager's channel slots instead.
	var slots := pens.get_recommended_locations()
	var player_slots := pens.slots_used()
	var competitors: Array = _sim_bridge.get_competitor_farms()
	var active_ids: Array = []

	var shown: Array = competitors.slice(0, max_competitors)
	for i in shown.size():
		var comp_dict: Dictionary = shown[i]
		var comp_id: String = str(comp_dict.get("id", ""))
		var slot := farm_slot(i, player_slots, slots.size())
		if comp_id.is_empty() or slot < 0:
			continue

		active_ids.append(comp_id)

		if not _competitor_farms.has(comp_id):
			_spawn_competitor_farm(comp_id, str(comp_dict.get("name", "Unknown")), slots[slot],
				float(comp_dict.get("market_share", 0.0)))

	# Remove farms for competitors that no longer exist or have lost their slot
	var to_remove: Array = []
	for comp_id in _competitor_farms.keys():
		if comp_id not in active_ids:
			to_remove.append(comp_id)
	for cid in to_remove:
		_remove_competitor_farm(cid)

## The channel slot of the rival at `index`: rivals fill the slots from the
## far end and the player's sites from the near end, so a slot the player's
## sites reach is not used and a rival never sits on a player's pen. -1 when
## there is no room. A rival farm fits in one slot (rival_pen_count).
static func farm_slot(index: int, player_slots: int, slot_count: int) -> int:
	var slot := slot_count - 1 - index
	return slot if slot >= player_slots else -1

## Pens of a rival's farm, fixed per competitor across runs: two rows of
## two, or one row of two. Either fits in one channel slot.
static func rival_pen_count(comp_id: String) -> int:
	return 2 if absi(hash(comp_id)) % 3 == 0 else 4

func _spawn_competitor_farm(comp_id: String, name: String, position: Vector3, market_share: float) -> void:
	var pens := _fjord_env.get_node_or_null("FishPenManager") as FishPenManager
	var band := band_color_for(comp_id)
	var count := rival_pen_count(comp_id)
	# A plain root holds the farm and its barge; the farm lies along the
	# channel like the player's sites (FishPenManager.channel_yaw_at).
	var farm_root := Node3D.new()
	farm_root.name = "CompetitorFarm_%s" % comp_id
	_fjord_env.add_child(farm_root)
	farm_root.global_position = position
	var yaw := pens.channel_yaw_at(position.z) if pens != null else 0.0
	var site := FarmSite.new()
	site.name = "Site"
	farm_root.add_child(site)
	site.rotation.y = yaw
	var barge_side := _barge_side(position)
	site.build(comp_id, count, {
		"barge_side": barge_side,
		"buoy_color": MooringGrid.BUOY_ORANGE,
		"band_color": band,
		"net_opacity": RIVAL_NET_OPACITY,
		"health_rings": false,
		"pen_numbers": false,
		"bird_net": FishPenManager.bird_net_for(comp_id),
		"label": name,
		"label_color": band.lerp(Color.WHITE, 0.35),
		"label_range": RIVAL_LABEL_RANGE,
	})
	for cage in site.cages:
		_style_rival_pen(cage, band)
	var barge := _moor_barge(site, barge_side)

	_competitor_farms[comp_id] = {
		"root": farm_root,
		"site": site,
		"cages": site.cages,
		"label": site.label,
		"mooring": site.grid,
		"barge": barge,
	}

	competitor_farm_added.emit(comp_id)

## The barge side of a rival farm: away from the fairway (the shore side).
func _barge_side(position: Vector3) -> float:
	var terrain := _fjord_env.get_node_or_null("Terrain") as FjordTerrain
	if terrain == null:
		return 1.0
	var to_fairway := terrain.channel_center_x(position.z) - position.x
	return -signf(to_fairway) if absf(to_fairway) >= 1.0 else 1.0

## The rival's feed barge lies moored beside its grid, its feed side
## (starboard, +X) towards the pens, with hoses to each of them.
func _moor_barge(site: FarmSite, side: float) -> VesselModel:
	if feed_barge_scene == null:
		return null
	var barge := feed_barge_scene.instantiate() as VesselModel
	if barge == null:
		return null
	barge.name = "FeedBarge"
	site.add_child(barge)
	var half := site.half_extent()
	barge.position = Vector3(side * (half.x + FishPenManager.BARGE_GRID_GAP_M + barge.beam_m * 0.5), 0.0, 0.0)
	barge.rotation.y = 0.0 if side < 0.0 else PI
	site.attach_hoses(barge.global_position, barge.length_m, barge.beam_m)
	return barge

## Rival pens are the player's pen asset with the owner's colour band, a
## quieter net and no health ring (the player cannot see a rival's fish
## health). No material is overridden: the band and the net opacity are
## instance parameters on the shared materials (roadmap G1-07, G2-04).
func _style_rival_pen(cage: Node3D, band: Color) -> void:
	if not (cage is PenCage):
		return
	var pen := cage as PenCage
	pen.set_owner_band(band)
	pen.set_net_opacity(RIVAL_NET_OPACITY)
	pen.show_health_ring(false)

## Net opacity of rival pens (the player's own nets draw at 1.0).
const RIVAL_NET_OPACITY := 0.5
## Rival names fade out sooner than the player's own site labels.
const RIVAL_LABEL_RANGE := 1000.0

## Rival band colours: saturated equipment hues that read against dark water
## and black HDPE, none of them the health-ring green/amber/red.
const BAND_PALETTE := [
	Color(0.910, 0.725, 0.165),  # buoy yellow #E8B92A
	Color(0.180, 0.561, 0.639),  # teal
	Color(0.851, 0.345, 0.231),  # signal orange-red #D9583B
	Color(0.478, 0.373, 0.659),  # violet
	Color(0.180, 0.365, 0.659),  # blue
	Color(0.788, 0.800, 0.776),  # light grey #C9CCC6
	Color(0.878, 0.482, 0.125),  # orange
	Color(0.553, 0.663, 0.282),  # moss
]

## A rival's band colour, fixed per competitor id across runs.
static func band_color_for(comp_id: String) -> Color:
	return BAND_PALETTE[absi(hash(comp_id)) % BAND_PALETTE.size()]

func _remove_competitor_farm(comp_id: String) -> void:
	if not _competitor_farms.has(comp_id):
		return
	var data: Dictionary = _competitor_farms[comp_id]
	if data.root:
		data.root.queue_free()
	_competitor_farms.erase(comp_id)

## Competitor ids with a farm drawn in the fjord.
func farm_ids() -> Array:
	return _competitor_farms.keys()
