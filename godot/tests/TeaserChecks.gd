extends RefCounted
## F2-23 acceptance 6 (farm-types spec §6.1 item 1): the teaser in the real
## game, headless. UiFlowTests runs these through run(self) and counts them
## with its own _check.
##
## 0. The rule (TeaserDirector.due) on the bridge's own tutorial statuses:
##    only Første sesong; nothing through day 150; a "do" step holds it back,
##    a wait step ("grow", "sale") or no tutorial lets it play; never from day
##    300 on; never twice; never on a timeline first seen after day 150.
## 1. A Første sesong game: nothing through day 150; on day 151 the first
##    step (a "do" step) holds it back; with the tutorial skipped (no step
##    active) it plays at once: the clock held, the UI aside, a rival's
##    yellow hull towed past the mouth toward hav_1, a news card that offers
##    "Fortsett" only (no build). The sim day stands while it runs; Esc skips
##    it and gives back the clock, the UI, the camera's own view and its
##    keys, and the engine state hash is the one before it (it changed
##    nothing).
## 2. It plays once: the days after it bring nothing.
## 3. A timeline first seen after day 150 (a save loaded on day 154) never
##    plays it.
## 4. Another scenario (po3_red_sone) never plays it.
## 5. With reduced motion it is a still: the frame cross-fades for 2 s, the
##    camera holds, the card; a click skips it.
## 6. Wave4b int review: due under an open critical event card it waits (the
##    card's Esc answers the card) and plays once the card has closed; a card
##    that comes up while it plays holds it still, its overlay aside.
## 7. Wave4b int review: a save of another Første sesong game loaded on day
##    160 over a game armed on day 100 is a new timeline, first seen after
##    day 150: it never plays.

const SCENARIO := "forste_sesong"
const SEED := 42
const OTHER_SCENARIO := "po3_red_sone"
const MAX_SECONDS := 20.0
const CROSSFADE_S := 2.0
## The mouth's sill (FJORD_RENDERER: z = -1300; the open sea lies at -Z).
const MOUTH_Z := -1300.0
const HAV_1 := Vector2(0.0, -5200.0)
## The world a cinematic borrows, read the same way for both (world_state).
const FIRST_SIGHT_CHECKS := preload("res://tests/FirstSightChecks.gd")

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	TeaserDirector.force_auto_play(true)
	MotionPrefs.force(false)
	_rules()
	await _first_season()
	await _first_seen_late()
	await _other_scenario()
	await _reduced()
	await _waits_for_an_open_card()
	await _a_loaded_game_is_a_new_timeline()
	MotionPrefs.force(null)
	TeaserDirector.force_auto_play(null)


# ── 0. The rule ──────────────────────────────────────────────────────────────

func _rules() -> void:
	var cfg := _teaser_data()
	_check(str(cfg.get("scenario", "")) == SCENARIO and int(cfg.get("after_day", -1)) == 150 and int(cfg.get("skip_from_day", -1)) == 300,
		"F2-23 teaser: data plays it in %s after day %d, never from day %d" % [cfg.get("scenario"), int(cfg.get("after_day", -1)), int(cfg.get("skip_from_day", -1))])
	var off := {"active": false}
	var doing := {"active": true, "kind": "do", "id": "open_pens"}
	var waiting := {"active": true, "kind": "wait", "id": "grow"}
	var cases := [
		[SCENARIO, 150, off, true, false, TeaserDirector.Due.WAIT, "day 150"],
		[SCENARIO, 151, off, true, false, TeaserDirector.Due.PLAY, "day 151, no tutorial"],
		[SCENARIO, 151, waiting, true, false, TeaserDirector.Due.PLAY, "day 151, a wait step (grow)"],
		[SCENARIO, 151, doing, true, false, TeaserDirector.Due.WAIT, "day 151, a do step"],
		[SCENARIO, 299, doing, true, false, TeaserDirector.Due.WAIT, "day 299, a do step"],
		[SCENARIO, 300, off, true, false, TeaserDirector.Due.NEVER, "day 300"],
		[SCENARIO, 200, off, true, true, TeaserDirector.Due.NEVER, "played already"],
		[SCENARIO, 200, off, false, false, TeaserDirector.Due.NEVER, "a timeline first seen after day 150"],
		[OTHER_SCENARIO, 200, off, true, false, TeaserDirector.Due.NEVER, "another scenario"],
		["", 200, off, true, false, TeaserDirector.Due.NEVER, "no scenario"],
	]
	var wrong: Array[String] = []
	for c: Array in cases:
		var got := TeaserDirector.due(cfg, str(c[0]), int(c[1]), c[2], bool(c[3]), bool(c[4]))
		if got != int(c[5]):
			wrong.append("%s: %d (want %d)" % [c[6], got, int(c[5])])
	_check(wrong.is_empty(), "F2-23 teaser: due() follows spec §6.1 item 1 in %d cases %s" % [cases.size(), ", ".join(wrong)])


# ── 1-2. Første sesong ───────────────────────────────────────────────────────

func _first_season() -> void:
	var sim: Node = t._sim
	_check(sim.start_qa_scenario(SCENARIO, SEED), "F2-23 teaser: %s starts" % SCENARIO)
	sim.set_paused(true)
	var scene := await _scene()
	var manager: FarmFacilityVisualManager = scene.get_node("FjordEnvironment/FarmFacilityVisualManager")
	var cam: FjordCamera = scene.get_node("FjordEnvironment/FjordCamera")
	var teaser: TeaserDirector = manager.teaser
	var ui: CanvasLayer = scene.get_node("UILayer")
	cam.auto_orbit_enabled = false
	_check(teaser != null and teaser.armed and not teaser.playing, "F2-23 teaser: a new Første sesong game arms it (day %d)" % sim.current_day)

	await _advance(sim, 150)
	_check(sim.current_day == 150 and not teaser.playing and teaser.plays == 0, "F2-23 teaser: nothing through day 150 (day %d)" % sim.current_day)
	await _advance(sim, 1)
	var status: Dictionary = sim.get_tutorial_status()
	_check(sim.current_day == 151 and bool(status.get("active", false)) and str(status.get("kind", "")) == "do" and not teaser.playing,
		"F2-23 teaser: on day 151 a tutorial step that asks for something (%s, %s) holds it back" % [status.get("id", ""), status.get("kind", "")])

	# The player's own view, settled, before it (no fly-to under way).
	cam.look_at_position(cam.home_focus)
	cam._snap()
	await t.get_tree().process_frame
	var view_before := cam.global_transform
	var fov_before := cam.fov
	var keys_before := cam.input_enabled
	# What it borrows, as it stands before it (wave4b int review): the hour
	# held still, the sim's weather settled, the mood, the sim weather on.
	var env: Node = scene.get_node("FjordEnvironment")
	var vista: Node = scene.get_node("FjordVistaBridge")
	(env.get("time_system") as TimeOfDay).paused = true
	var weather: WeatherController = env.get("weather_system")
	var blend := weather.transition_duration
	weather.transition_duration = 0.0
	vista.call("_sync_environment", sim)
	weather.transition_duration = blend
	var world_before: Dictionary = FIRST_SIGHT_CHECKS.world_state(env, vista)
	# No step active (the tutorial skipped): it plays at once.
	sim.skip_tutorial()
	var digest: String = sim.qa_state_digest()
	var day: int = sim.current_day
	for i in 3:
		await t.get_tree().process_frame
	_check(teaser.playing and teaser.plays == 1 and not teaser.reduced, "F2-23 teaser: with no tutorial step active it plays on day %d" % day)
	_check(teaser.duration <= MAX_SECONDS + 1e-3, "F2-23 teaser: it lasts %.1f s (<= 20 s)" % teaser.duration)
	_check(sim.is_clock_held(TeaserDirector.CLOCK_HOLD) and not ui.visible and teaser.overlay != null and not cam.input_enabled,
		"F2-23 teaser: it holds the clock, the game's UI steps aside and the camera takes no keys")
	var offered: Array = teaser.overlay.offered_buttons().map(func(b: Button) -> String: return String(b.name))
	_check(offered == ["ContinueButton"], "F2-23 teaser: its news card offers %s only: it never offers a build" % str(offered))
	# The tow: a yellow hull and three tugs, beyond the mouth, bound for hav_1.
	var hull := teaser.tow.get_node_or_null("RivalHull") as OffshoreUnitModel if teaser.tow != null else null
	var tugs := 0
	for child in (teaser.tow.get_children() if teaser.tow != null else []):
		tugs += 1 if String(child.name).begins_with("Tug") else 0
	teaser.manual = true
	teaser.seek(0.0)
	var start := Vector2(hull.global_position.x, hull.global_position.z) if hull != null else Vector2.INF
	var start_screen := cam.unproject_position(hull.global_position) if hull != null else Vector2.INF
	teaser.seek(teaser.duration - 0.5)
	var end := Vector2(hull.global_position.x, hull.global_position.z) if hull != null else Vector2.INF
	var view := t.get_viewport().get_visible_rect().size
	_check(hull != null and tugs == 3 and start.y < MOUTH_Z - 1000.0 and end.distance_to(HAV_1) < start.distance_to(HAV_1)
			and Rect2(Vector2.ZERO, view).has_point(start_screen),
		"F2-23 teaser: a rival's hull and %d tugs pass beyond the mouth (z %.0f) on screen, bound for hav_1 (%.0f m -> %.0f m off it)"
		% [tugs, start.y, start.distance_to(HAV_1), end.distance_to(HAV_1)])
	_check(teaser.overlay.card.visible and teaser.overlay.card.modulate.a > 0.99, "F2-23 teaser: the news card follows")
	# The clock held: a clock running at 30x (18 days in 0.6 s) does not move the sim day.
	teaser.manual = false
	teaser.seek(0.5)
	sim.set_time_speed(30.0)
	sim.set_paused(false)
	await t.get_tree().create_timer(0.6).timeout
	_check(sim.current_day == day and teaser.time > 0.5, "F2-23 teaser: while it plays the sim day stands (day %d at 30x; %.2f s in)" % [sim.current_day, teaser.time])
	sim.set_paused(true)
	sim.set_time_speed(1.0)
	# Esc skips it, as a player presses it.
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	_check(not teaser.playing and teaser.tow == null and not sim.is_clock_held(TeaserDirector.CLOCK_HOLD) and ui.visible
			and cam.input_enabled == keys_before,
		"F2-23 teaser: Esc skips it: the tow is gone, the clock, the UI and the camera's keys are given back")
	_check(cam.global_transform.is_equal_approx(view_before) and is_equal_approx(cam.fov, fov_before),
		"F2-23 teaser: the camera is back on the player's own view")
	var world_after: Dictionary = FIRST_SIGHT_CHECKS.world_state(env, vista)
	_check(world_after == world_before,
		"F2-23 teaser: Esc gives the world back as it was: the hour, the sim's weather at once, the mood, the sim weather on (%s; before %s)"
		% [str(world_after), str(world_before)])
	_check(sim.current_day == day and sim.qa_state_digest() == digest,
		"F2-23 teaser: the sim day and the engine state hash stand where they stood (day %d)" % sim.current_day)
	# 2. Once.
	await _advance(sim, 3)
	_check(not teaser.playing and teaser.plays == 1 and teaser.played, "F2-23 teaser: it plays once (day %d: %d plays)" % [sim.current_day, teaser.plays])
	scene.queue_free()
	await t.get_tree().process_frame


# ── 3. A timeline first seen after day 150 ───────────────────────────────────

func _first_seen_late() -> void:
	var sim: Node = t._sim
	# The same game, its scene built again on day 154 (a save loaded there).
	var scene := await _scene()
	var teaser: TeaserDirector = (scene.get_node("FjordEnvironment/FarmFacilityVisualManager") as FarmFacilityVisualManager).teaser
	await _advance(sim, 2)
	_check(sim.current_day > 150 and not teaser.armed and not teaser.playing and teaser.plays == 0,
		"F2-23 teaser: a game first seen on day %d (a save loaded after day 150) never plays it" % sim.current_day)
	scene.queue_free()
	await t.get_tree().process_frame


# ── 4. Another scenario ──────────────────────────────────────────────────────

func _other_scenario() -> void:
	var sim: Node = t._sim
	_check(sim.start_qa_scenario(OTHER_SCENARIO, SEED), "F2-23 teaser: %s starts" % OTHER_SCENARIO)
	sim.set_paused(true)
	var scene := await _scene()
	var teaser: TeaserDirector = (scene.get_node("FjordEnvironment/FarmFacilityVisualManager") as FarmFacilityVisualManager).teaser
	await _advance(sim, 1)
	var armed := teaser.armed
	await _advance(sim, 151)
	_check(armed and sim.current_day > 150 and not teaser.playing and teaser.plays == 0,
		"F2-23 teaser: %s, seen from day 1, plays nothing on day %d" % [OTHER_SCENARIO, sim.current_day])
	scene.queue_free()
	await t.get_tree().process_frame


# ── 5. Reduced motion ────────────────────────────────────────────────────────

func _reduced() -> void:
	var sim: Node = t._sim
	MotionPrefs.force(true)
	_check(sim.start_qa_scenario(SCENARIO, SEED), "F2-23 teaser: %s starts again" % SCENARIO)
	sim.set_paused(true)
	sim.skip_tutorial()
	var scene := await _scene()
	var manager: FarmFacilityVisualManager = scene.get_node("FjordEnvironment/FarmFacilityVisualManager")
	var cam: FjordCamera = scene.get_node("FjordEnvironment/FjordCamera")
	var teaser: TeaserDirector = manager.teaser
	cam.auto_orbit_enabled = false
	await _advance(sim, 1)
	# The day it becomes due: the hash read before the deferred start.
	sim.advance_days(150)
	var digest: String = sim.qa_state_digest()
	for i in 3:
		await t.get_tree().process_frame
	_check(teaser.playing and teaser.reduced, "F2-23 teaser: with reduced motion it plays as a still (day %d)" % sim.current_day)
	teaser.manual = true
	teaser.seek(0.0)
	var fade_start: float = teaser.overlay.still.modulate.a if teaser.overlay != null else -1.0
	teaser.seek(CROSSFADE_S)
	var fade_end: float = teaser.overlay.still.modulate.a if teaser.overlay != null else -1.0
	var pose_a := cam.global_transform
	teaser.seek(CROSSFADE_S + 2.0)
	var pose_b := cam.global_transform
	var card: float = teaser.overlay.card.modulate.a if teaser.overlay != null else 0.0
	_check(is_equal_approx(fade_start, 1.0) and fade_end <= 0.001 and pose_a.is_equal_approx(pose_b) and card > 0.99 and teaser.duration <= 8.0,
		"F2-23 teaser: the still cross-fades in %.1f s (alpha %.2f -> %.2f), the camera holds, the card shows; %.1f s in all"
		% [CROSSFADE_S, fade_start, fade_end, teaser.duration])
	teaser.manual = false
	var view := t.get_viewport().get_visible_rect().size
	await t._click_at(Vector2(view.x * 0.75, view.y * 0.35))
	await t.get_tree().process_frame
	_check(not teaser.playing and not sim.is_clock_held(TeaserDirector.CLOCK_HOLD) and sim.qa_state_digest() == digest,
		"F2-23 teaser: a click skips the still, and it changed nothing in the engine")
	MotionPrefs.force(false)
	scene.queue_free()
	await t.get_tree().process_frame


# ── 6. An open card ──────────────────────────────────────────────────────────

func _waits_for_an_open_card() -> void:
	var sim: Node = t._sim
	MotionPrefs.force(false)
	GameplayPrefs.force_auto_pause(true)
	_check(sim.start_qa_scenario(SCENARIO, SEED), "F2-23 teaser: %s starts (a card open when it is due)" % SCENARIO)
	sim.set_paused(true)
	sim.skip_tutorial()
	var scene := await _scene()
	var manager: FarmFacilityVisualManager = scene.get_node("FjordEnvironment/FarmFacilityVisualManager")
	var teaser: TeaserDirector = manager.teaser
	var card: Control = scene.get_node("%CriticalEventCard")
	var ui: CanvasLayer = scene.get_node("UILayer")
	(scene.get_node("FjordEnvironment/FjordCamera") as FjordCamera).auto_orbit_enabled = false
	await _advance(sim, 150)
	await t._drain_cards(card)
	# A critical event's card is up as day 151 makes it due.
	sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen kritisk lavt 3,1 mg/L")
	await _advance(sim, 1)
	for i in 3:
		await t.get_tree().process_frame
	_check(card.is_open() and sim.current_day == 151 and not teaser.playing and teaser.plays == 0 and teaser.overlay == null and ui.visible,
		"F2-23 teaser: due on day %d under an open critical event card, it waits (the UI as it was)" % sim.current_day)
	await t._press_key(KEY_ESCAPE)
	for i in 3:
		await t.get_tree().process_frame
	_check(not card.is_open() and teaser.playing and teaser.plays == 1,
		"F2-23 teaser: Esc answers the card, and the teaser plays once it has closed")
	# A card that comes up while it plays holds it still, its overlay aside.
	sim.publish_condition_event("nets_critical", "critical", "", "CI: nøter kritisk slitt 31 %")
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	var held_at := teaser.time
	for i in 4:
		await t.get_tree().process_frame
	var stood: bool = card.is_open() and teaser.playing and teaser.overlay != null and not teaser.overlay.visible \
		and is_equal_approx(teaser.time, held_at) and sim.is_clock_held(TeaserDirector.CLOCK_HOLD)
	await t._press_key(KEY_ESCAPE)
	for i in 3:
		await t.get_tree().process_frame
	_check(stood and not card.is_open() and teaser.playing and teaser.overlay.visible and teaser.time > held_at,
		"F2-23 teaser: a card that comes up while it plays holds it still (%.2f s), overlay aside, until Esc answers the card" % held_at)
	teaser.skip()
	await t._drain_cards(card)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await t.get_tree().process_frame


# ── 7. A save of another game loaded ─────────────────────────────────────────

func _a_loaded_game_is_a_new_timeline() -> void:
	var sim: Node = t._sim
	MotionPrefs.force(false)
	# Another Første sesong game, saved on day 160.
	_check(sim.start_qa_scenario(SCENARIO, 7), "F2-23 teaser: another %s game starts" % SCENARIO)
	sim.set_paused(true)
	sim.skip_tutorial()
	sim.advance_days(160)
	var saved := bool(sim.save_game("teaser_other_game"))
	# The game the player is in, seen from its start: armed, not played by day 100.
	_check(sim.start_qa_scenario(SCENARIO, SEED), "F2-23 teaser: %s starts (the load case)" % SCENARIO)
	sim.set_paused(true)
	var scene := await _scene()
	var teaser: TeaserDirector = (scene.get_node("FjordEnvironment/FarmFacilityVisualManager") as FarmFacilityVisualManager).teaser
	await _advance(sim, 100)
	var armed_before := teaser.armed and not teaser.played
	var loaded := bool(sim.load_game(str(t._find_save("teaser_other_game").get("file", ""))))
	for i in 3:
		await t.get_tree().process_frame
	sim.skip_tutorial()
	await _advance(sim, 3)
	_check(saved and loaded and armed_before and sim.current_day >= 160 and not teaser.armed and not teaser.playing and teaser.plays == 0,
		"F2-23 teaser: another game's save loaded on day %d over one armed on day 100 is first seen after day 150: it never plays" % sim.current_day)
	if teaser.playing:
		teaser.skip()
	t._clear_test_saves()
	scene.queue_free()
	await t.get_tree().process_frame


# ── Helpers ──────────────────────────────────────────────────────────────────

## The game scene built over the running game, which it sees at its current
## day (a state refresh, as a new game's or a loaded save's first one).
func _scene() -> Node:
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	t._sim.set_paused(true)
	t._sim._refresh_state()
	await t.get_tree().process_frame
	return scene


## Advance the engine `days` (as the skip buttons do) and let the refresh land.
func _advance(sim: Node, days: int) -> void:
	sim.advance_days(days)
	for i in 3:
		await t.get_tree().process_frame


func _teaser_data() -> Dictionary:
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(TeaserDirector.DATA_PATH))
	return (parsed as Dictionary).get("teaser", {}) if parsed is Dictionary else {}


func _check(condition: bool, label: String) -> void:
	t._check(condition, label)
