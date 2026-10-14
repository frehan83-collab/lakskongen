class_name LandCampusLayout
extends RefCounted
## Where the land-kit pieces stand on the land pads and where the offshore
## units float (farm-types spec §4 and §5.1 "Layouts"). QA only for now:
## the F2-18 gray box and the F2-25 land-kit preview both read it, so the
## gray-box read gate measures the same campus the kit is built for. F2-20's
## LandCampusBuilder takes it over once the sim plots exist (F2-03), and the
## plot centres then come from godot/data/site_plots.json.
##
## Every placement is a Dictionary:
##   asset  kit asset id (spec §5.1 / §5.2), e.g. "tank_growout"
##   plot   plot id ("pad_a", "pad_b", "pad_delta", "off_1", "off_2")
##   pos    world position of the asset origin (Vector3; y = its base)
##   yaw    rotation about +Y in radians (model +Z turns to world (sin, 0, cos))
##   lot    lot index on the plot (-1 for shared site pieces)
##   size   Vector3 footprint (x, height, z) in the model's own axes
## Model axes follow the Blender exports: +Y up, the seaward/front face +Z.

## Pad top: the Indre Harøy plan level [L:R13] (spec §4).
const PAD_TOP_Y := 3.6
## Quay deck height (spec §5.1 `quay`).
const QUAY_DECK_Y := 3.0
## Spec §4 plot catalogue (procedural fallback positions, metres). pad_a
## sits where the F2-18 read gate moved it, (-450, -1720) instead of
## revision 2's (-700, -1650): there 70% of the campus hid behind the
## western headland at the home view (docs/run/evidence/f2_18_read_gate.md).
const PLOTS := {
	"pad_a": {"kind": "land", "centre": Vector2(-450.0, -1720.0), "size": Vector2(640.0, 420.0), "lots": 4, "sea_side": 1.0},
	"pad_b": {"kind": "land", "centre": Vector2(750.0, -1640.0), "size": Vector2(480.0, 360.0), "lots": 2, "sea_side": -1.0},
	"pad_delta": {"kind": "land", "centre": Vector2(0.0, 1450.0), "size": Vector2(260.0, 180.0), "lots": 0, "sea_side": 1.0},
	"off_1": {"kind": "offshore", "centre": Vector2(-450.0, -2750.0), "unit": "of1_class"},
	"off_2": {"kind": "offshore", "centre": Vector2(850.0, -3400.0), "unit": "havfarm_class"},
}

## Tank geometry (spec §5.1): grow-out Ø28 x 13 m, pre-grow-out / post-smolt
## Ø16 x 9 m.
const GROWOUT_D := 28.0
const GROWOUT_H := 13.0
const PREGROW_D := 16.0
const PREGROW_H := 9.0
## Full HFS lot: two rows of 6 grow-out tanks 4 m apart (each row 188 m),
## a hall between the rows, a pre-grow-out tank at the end of each row:
## about 208 x 90 m.
const LOT_TANKS_PER_ROW := 6
const LOT_TANK_GAP := 4.0
const LOT_ROW_OFFSET := 31.0
const LOT_SIZE := Vector2(208.0, 90.0)
## Process hall bays (30 x 20 x 10) along the lot axis between the rows.
const HALL_BAY := Vector3(30.0, 10.0, 20.0)
const HALL_BAYS_PER_LOT := 6
## Post-smolt module: 2 pre-grow tanks under a lean-to bay; modules side by side.
const MODULE_TANK_PITCH := 20.0
const MODULE_PITCH := 44.0
## Pipe bridge spans (spec §5.1: 25 m span, legs 6-14 m).
const BRIDGE_SPAN := 25.0
const BRIDGE_DECK_ABOVE_PAD := 10.0
## OF1-class (spec §5.2): circumradius 55, 25 m above the waterline.
const OF1_RADIUS := 55.0
const OF1_FREEBOARD := 25.0
## Havfarm-class (spec §5.2): 385 x 59.5 m hull, freeboard 11 m.
const HAVFARM_SIZE := Vector3(385.0, 11.0, 59.5)


## QA only: plot id -> Vector2 centre that replaces the spec's (the read gate
## tries alternative pad positions with it). Empty in normal use.
static var centre_overrides: Dictionary = {}


## Every placement on every plot, in a fixed order.
static func all_placements() -> Array:
	var out: Array = []
	for plot_id in ["pad_a", "pad_b", "pad_delta", "off_1", "off_2"]:
		out.append_array(plot_placements(plot_id))
	return out


static func plot_placements(plot_id: String) -> Array:
	match plot_id:
		"pad_a":
			return _pad_a()
		"pad_b":
			return _pad_b()
		"pad_delta":
			return _pad_delta()
		"off_1", "off_2":
			var unit: String = PLOTS[plot_id]["unit"]
			var size := Vector3(2.0 * OF1_RADIUS, OF1_FREEBOARD, 2.0 * OF1_RADIUS) if unit == "of1_class" else HAVFARM_SIZE
			return [_p(unit, plot_id, plot_centre(plot_id), 0.0, -1, size)]
	return []


## Plot centre in world space (y = pad top for land, waterline offshore).
static func plot_centre(plot_id: String) -> Vector3:
	var plot: Dictionary = PLOTS[plot_id]
	var c: Vector2 = centre_overrides.get(plot_id, plot["centre"])
	return Vector3(c.x, PAD_TOP_Y if plot["kind"] == "land" else 0.0, c.y)


## Tanks on a plot (grow-out and pre-grow), for counts and the read gate.
static func tank_count(plot_id: String, asset: String = "") -> int:
	var n := 0
	for p in plot_placements(plot_id):
		var a: String = p["asset"]
		if (asset == "" and a.begins_with("tank_")) or a == asset:
			n += 1
	return n


# ── Pads ──────────────────────────────────────────────────────────────────────

## Storholmen: the hero campus. Four full lots on the far (mouth) side, the
## post-smolt modules and utilities on the near side, the harvest hall and
## the quay on the fairway (east) edge, the orange pipe bridge from the quay
## to the lots, the intake basin in the south-east corner.
static func _pad_a() -> Array:
	var out: Array = []
	var c := plot_centre("pad_a")
	var lots := [Vector2(-201.0, -150.0), Vector2(37.0, -150.0), Vector2(-201.0, -45.0), Vector2(37.0, -45.0)]
	for i in lots.size():
		out.append_array(_lot("pad_a", i, c + Vector3(lots[i].x, 0.0, lots[i].y)))
	for k in 4:
		out.append_array(_post_smolt_module("pad_a", c + Vector3(-290.0 + MODULE_PITCH * k, 0.0, 62.0)))
	out.append(_p("harvest_hall", "pad_a", c + Vector3(230.0, 0.0, -30.0), PI * 0.5, -1, Vector3(120.0, 15.0, 60.0)))
	out.append(_p("quay", "pad_a", Vector3(c.x + 332.5, QUAY_DECK_Y, c.z - 40.0), PI * 0.5, -1, Vector3(120.0, 3.0, 25.0)))
	out.append_array(_bridge("pad_a", Vector3(c.x + 330.0, 0.0, c.z - 97.5), -1.0, 8))
	out.append(_p("intake_basin", "pad_a", c + Vector3(285.0, 0.0, 150.0), PI * 0.5, -1, Vector3(90.0, 3.0, 60.0)))
	out.append(_p("feed_silo_land", "pad_a", c + Vector3(165.0, 0.0, 62.0), 0.0, -1, Vector3(10.0, 14.0, 10.0)))
	out.append(_p("sludge_station", "pad_a", c + Vector3(195.0, 0.0, 150.0), 0.0, -1, Vector3(20.0, 8.0, 12.0)))
	out.append(_p("substation", "pad_a", c + Vector3(-280.0, 0.0, 170.0), 0.0, -1, Vector3(18.0, 6.0, 10.0)))
	for k in 3:
		out.append(_p("genset_container", "pad_a", c + Vector3(-235.0 + 16.0 * k, 0.0, 178.0), 0.0, -1, Vector3(12.19, 2.9, 2.44)))
	for k in 3:
		out.append(_p("lox_tank", "pad_a", c + Vector3(-150.0 + 6.0 * k, 0.0, 168.0), 0.0, -1, Vector3(3.2, 10.5, 3.2)))
	return out


## Skjærvika: a second campus of two lots, quay on the west (fairway) edge.
static func _pad_b() -> Array:
	var out: Array = []
	var c := plot_centre("pad_b")
	for i in 2:
		out.append_array(_lot("pad_b", i, c + Vector3(60.0, 0.0, -100.0 + 105.0 * i)))
	out.append(_p("harvest_hall", "pad_b", c + Vector3(-170.0, 0.0, 20.0), -PI * 0.5, -1, Vector3(120.0, 15.0, 60.0)))
	out.append(_p("quay", "pad_b", Vector3(c.x - 252.5, QUAY_DECK_Y, c.z - 50.0), -PI * 0.5, -1, Vector3(120.0, 3.0, 25.0)))
	out.append_array(_bridge("pad_b", Vector3(c.x - 250.0, 0.0, c.z - 47.5), 1.0, 8))
	out.append(_p("intake_basin", "pad_b", c + Vector3(-205.0, 0.0, 135.0), -PI * 0.5, -1, Vector3(90.0, 3.0, 60.0)))
	out.append(_p("substation", "pad_b", c + Vector3(190.0, 0.0, 150.0), 0.0, -1, Vector3(18.0, 6.0, 10.0)))
	for k in 2:
		out.append(_p("genset_container", "pad_b", c + Vector3(140.0 - 16.0 * k, 0.0, 155.0), 0.0, -1, Vector3(12.19, 2.9, 2.44)))
	for k in 2:
		out.append(_p("lox_tank", "pad_b", c + Vector3(90.0 - 6.0 * k, 0.0, 150.0), 0.0, -1, Vector3(3.2, 10.5, 3.2)))
	out.append(_p("feed_silo_land", "pad_b", c + Vector3(-120.0, 0.0, 60.0), 0.0, -1, Vector3(10.0, 14.0, 10.0)))
	return out


## Elveøyra: up to four post-smolt modules on the delta, a river intake
## (weir and screen house) beside a fjord-basin intake.
static func _pad_delta() -> Array:
	var out: Array = []
	var c := plot_centre("pad_delta")
	for k in 4:
		out.append_array(_post_smolt_module("pad_delta", c + Vector3(-88.0 + MODULE_PITCH * k, 0.0, -20.0)))
	out.append(_p("river_intake", "pad_delta", c + Vector3(-60.0, 0.0, 70.0), 0.0, -1, Vector3(30.0, 5.0, 8.0)))
	out.append(_p("lox_tank", "pad_delta", c + Vector3(80.0, 0.0, 60.0), 0.0, -1, Vector3(3.2, 10.5, 3.2)))
	out.append(_p("genset_container", "pad_delta", c + Vector3(40.0, 0.0, 66.0), 0.0, -1, Vector3(12.19, 2.9, 2.44)))
	out.append(_p("substation", "pad_delta", c + Vector3(100.0, 0.0, -70.0), 0.0, -1, Vector3(18.0, 6.0, 10.0)))
	return out


# ── Building blocks ───────────────────────────────────────────────────────────

## One full HFS lot centred at `centre`: rows along X, hall between them.
static func _lot(plot_id: String, lot: int, centre: Vector3) -> Array:
	var out: Array = []
	var pitch := GROWOUT_D + LOT_TANK_GAP
	var row_len := LOT_TANKS_PER_ROW * GROWOUT_D + (LOT_TANKS_PER_ROW - 1) * LOT_TANK_GAP
	# The lot is 208 m: 188 of grow-out row, 4 m, a 16 m pre-grow-out tank.
	var x0 := centre.x - LOT_SIZE.x * 0.5 + GROWOUT_D * 0.5
	for row: float in [-1.0, 1.0]:
		var z := centre.z + row * LOT_ROW_OFFSET
		for k in LOT_TANKS_PER_ROW:
			out.append(_p("tank_growout", plot_id, Vector3(x0 + pitch * k, centre.y, z), 0.0, lot, Vector3(GROWOUT_D, GROWOUT_H, GROWOUT_D)))
		var xp := centre.x - LOT_SIZE.x * 0.5 + row_len + LOT_TANK_GAP + PREGROW_D * 0.5
		out.append(_p("tank_pregrow", plot_id, Vector3(xp, centre.y, z), 0.0, lot, Vector3(PREGROW_D, PREGROW_H, PREGROW_D)))
	var hx0 := centre.x - LOT_SIZE.x * 0.5 + (row_len - HALL_BAYS_PER_LOT * HALL_BAY.x) * 0.5 + HALL_BAY.x * 0.5
	for k in HALL_BAYS_PER_LOT:
		out.append(_p("process_hall_bay", plot_id, Vector3(hx0 + HALL_BAY.x * k, centre.y, centre.z), 0.0, lot, HALL_BAY))
	return out


## A post-smolt module: two Ø16 tanks with their lean-to bay behind them.
static func _post_smolt_module(plot_id: String, origin: Vector3) -> Array:
	var out: Array = []
	for k in 2:
		out.append(_p("tank_pregrow", plot_id, origin + Vector3(MODULE_TANK_PITCH * k, 0.0, 0.0), 0.0, -1, Vector3(PREGROW_D, PREGROW_H, PREGROW_D)))
	out.append(_p("process_hall_bay", plot_id, origin + Vector3(MODULE_TANK_PITCH * 0.5, 0.0, 22.0), 0.0, -1, HALL_BAY))
	return out


## The orange pipe bridge: `spans` 25 m spans heading along +X * dir from
## `start` (the quay end) at a deck 10 m over the pad.
static func _bridge(plot_id: String, start: Vector3, dir: float, spans: int) -> Array:
	var out: Array = []
	for k in spans:
		var x := start.x + dir * (BRIDGE_SPAN * (k + 0.5))
		out.append(_p("pipe_bridge_span", plot_id, Vector3(x, PAD_TOP_Y, start.z), 0.0, -1, Vector3(BRIDGE_SPAN, BRIDGE_DECK_ABOVE_PAD, 3.5)))
	return out


static func _p(asset: String, plot_id: String, pos: Vector3, yaw: float, lot: int, size: Vector3) -> Dictionary:
	return {"asset": asset, "plot": plot_id, "pos": pos, "yaw": yaw, "lot": lot, "size": size}
