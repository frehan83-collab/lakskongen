class_name UnderwaterShots
extends RefCounted
## The underwater shots (roadmap G3-04): shot 11 and its variants, from inside
## the first stocked pen of the QA game, through ScreenshotHarness._shoot().
## The water is the sim's: the QA game's day sets the season's clarity
## (WaterClarity) and 11b adds a QA bloom at the site (GameBridge.SpawnQaBloom).
## A JSON report goes next to the frames (REPORT).

const REPORT := "11_underwater.json"
## [id, hour, season, weather (ScreenshotHarness WEATHER_*), tilt (deg), bloom severity]
const SHOTS := [
	{"id": "11_fjord_underwater", "hour": 12.0, "season": 0.5, "weather": 0, "tilt": 0.0, "bloom": 0.0},
	{"id": "11c_underwater_window", "hour": 12.0, "season": 0.5, "weather": 0, "tilt": 45.0, "bloom": 0.0},
	{"id": "11b_underwater_bloom", "hour": 12.0, "season": 0.5, "weather": 0, "tilt": 0.0, "bloom": 0.8},
]
const ZOOM_FARM := 2
const ZOOM_UNDERWATER := 3
const SETTLE_S := 2.5

var failures := PackedStringArray()
var report := {}


## Capture the underwater shots through `harness` (ScreenshotHarness).
func capture(harness, env: Node, game: Node) -> void:
	var cam: FjordCamera = env.get("camera") if env != null else null
	var pens := env.get_node_or_null("FishPenManager") as FishPenManager if env != null else null
	var director: EnvironmentDirector = env.get("director") if env != null else null
	if cam == null or pens == null or director == null or pens.get_pen_ids().is_empty():
		failures.append("11_fjord_underwater: the fjord scene, its pens or the director are missing")
		return
	var vista := game.find_child("FjordVistaBridge", true, false)
	if vista != null:
		vista.set("sim_weather_enabled", false)
	var target := stocked_pen(pens)
	if target.is_empty():
		failures.append("11_fjord_underwater: no stocked pen to dive into")
		return
	var entries := {}
	for shot: Dictionary in SHOTS:
		var id := String(shot["id"])
		await harness._configure_env(float(shot["season"]), float(shot["hour"]), int(shot["weather"]), ZOOM_FARM)
		_set_bloom(harness, String(target["site_id"]), float(shot["bloom"]))
		await harness.get_tree().create_timer(0.6).timeout
		cam.auto_orbit_enabled = false
		cam.look_at_position(target["center"], ZOOM_UNDERWATER)
		cam.set_pitch_offset(float(shot["tilt"]))
		cam.call("_snap")
		await harness.get_tree().create_timer(SETTLE_S).timeout
		var img: Image = await harness._shoot(id, true)
		if img == null:
			failures.append("%s was not captured" % id)
			continue
		var composed := director.get_composed()
		var view := env.get_node_or_null("UnderwaterView") as UnderwaterView
		var uw: Dictionary = composed.get("uw", {})
		entries[id] = {
			"camera": [cam.global_position.x, cam.global_position.y, cam.global_position.z],
			"depth_m": -cam.global_position.y,
			"from_pen_centre_m": Vector2(cam.global_position.x - target["center"].x, cam.global_position.z - target["center"].z).length(),
			"fog_density": composed.get("fog_density", 0.0),
			"exposure": composed.get("exposure", 0.0),
			"visibility_m": uw.get("visibility_m", 0.0),
			"horizontal": _arr(uw.get("horizontal", Vector3.ZERO)),
			"sun_at_depth": _arr(uw.get("sun_at_depth", Vector3.ZERO)),
			"shafts": view.shafts.visible if view != null and view.shafts != null else false,
			"shaft_count": view.shafts.multimesh.visible_instance_count if view != null and view.shafts != null else 0,
			"snow": view.snow.visible if view != null and view.snow != null else false,
		}
		print("[UnderwaterShots] %s: %s" % [id, JSON.stringify(entries[id])])
	_set_bloom(harness, String(target["site_id"]), 0.0)
	cam.set_pitch_offset(0.0)
	report = {"pen": {"site_id": target["site_id"], "pen": target["pen"]}, "shots": entries, "failures": failures}
	var path := ProjectSettings.globalize_path("res://screenshots/game").path_join(REPORT)
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(report, "\t"))


static func _arr(v: Vector3) -> Array:
	return [snappedf(v.x, 0.0001), snappedf(v.y, 0.0001), snappedf(v.z, 0.0001)]


## The first pen whose school shows fish: {"site_id", "pen", "center"}, or {}.
static func stocked_pen(pens: FishPenManager) -> Dictionary:
	for site_id in pens.get_pen_ids():
		var farm := pens.get_site(site_id)
		if farm == null or farm.is_ghost():
			continue
		var schools: Array = pens._pens[site_id].get("fish_schools", [])
		for k in schools.size():
			if FishSchool.shown_fish(schools[k]) > 0:
				var at := farm.pen_rest_position(k)
				return {"site_id": site_id, "pen": k, "center": Vector3(at.x, 0.0, at.z)}
	return {}


## A QA bloom of `severity` at the site (0 clears every QA bloom), and the
## state refresh that hands the sites' water to the view.
static func _set_bloom(harness, site_id: String, severity: float) -> void:
	var gb: Node = harness.get_node_or_null("/root/GameBridge")
	var sim: Node = harness.get_node_or_null("/root/SimBridge")
	if gb == null:
		return
	if gb.has_method("ClearQaBlooms"):
		gb.ClearQaBlooms()
	if severity > 0.0 and gb.has_method("SpawnQaBloom"):
		gb.SpawnQaBloom(site_id, severity, 21)
	# The blooms emit GameStateUpdated; refresh now so the vista's sync hands
	# the sites' water to the view before the frame.
	if sim != null and sim.has_method("_refresh_state"):
		sim.call("_refresh_state")
