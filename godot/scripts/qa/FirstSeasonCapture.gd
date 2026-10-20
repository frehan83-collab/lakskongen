extends "res://tests/UiFlowTests.gd"
## U2-02 frames: the first season's card and pointer at the steps that teach
## a screen, the skip question and the won season, captured from the real
## GameScene while the input-only player of FirstSeasonFlowChecks plays
## Første sesong (seed 7: it borrows at the funding step). The shots in
## godot/qa/shots.json for this scene (43_first_season_*) name the moment;
## the frames go to res://screenshots/game/43_first_season_*.png. Saves and
## the campaign are the UI tests' own files, never the player's. The
## checks the player makes on the way are printed as in UiFlowTests.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/FirstSeasonCapture.tscn
## Headless (no image data) it plays the same and only names the frames.

const SCENE_PATH := "res://qa/scenes/FirstSeasonCapture.tscn"
const SHOTS_FILE := "res://qa/shots.json"
const OUT_DIR := "res://screenshots/game/"
## Let the ring, the card's corner and the toasts settle before a frame (s).
const SETTLE_BEFORE_SHOT_S := 0.5

var _shot_of_moment: Dictionary = {}
var _captured: Array[String] = []
var _capture_failures := 0


func _ready() -> void:
	_go.call_deferred()


func _go() -> void:
	_sim = get_node("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	if mgr != null:
		mgr.UseSaveDirectory(TEST_SAVE_DIR)
	CampaignProgress.path_override = TEST_ROOT + "campaign.json"
	CampaignProgress.reset()
	TranslationServer.set_locale("no")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	for shot: Dictionary in _shots():
		_shot_of_moment[str(shot.get("state", ""))] = shot
	if not _shot_of_moment.is_empty() and not _headless():
		var res: Array = (_shot_of_moment.values()[0] as Dictionary).get("resolution", [1920, 1080])
		get_window().size = Vector2i(int(res[0]), int(res[1]))
	GameplayPrefs.force_tutorial(true)
	GameplayPrefs.force_auto_pause(true)
	MotionPrefs.force(true)   # the ring holds still: the frames compare
	var checks: RefCounted = preload("res://tests/FirstSeasonFlowChecks.gd").new()
	checks.t = self
	checks.on_moment = _on_moment
	await checks._a_skip_by_input_is_no_win()
	await checks._a_player_using_only_input_wins_the_first_season()
	MotionPrefs.force(null)
	GameplayPrefs.force_auto_pause(null)
	GameplayPrefs.force_tutorial(null)
	_clear_test_saves()
	CampaignProgress.reset()
	var missing := _shot_of_moment.values().map(func(s: Dictionary) -> String: return str(s["id"])) \
		.filter(func(id: String) -> bool: return not _captured.has(id))
	if not missing.is_empty():
		push_error("[FirstSeasonCapture] moments never reached: %s" % str(missing))
	print("[FirstSeasonCapture] done: %d of %d frames, %d capture failure(s), %d check failure(s)" % [
		_captured.size(), _shot_of_moment.size(), _capture_failures, _failures.size()])
	var bad := _capture_failures > 0 or not missing.is_empty() or not _failures.is_empty()
	get_tree().quit(1 if bad else 0)


func _shots() -> Array:
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(SHOTS_FILE))
	if not (data is Dictionary):
		push_error("[FirstSeasonCapture] %s does not parse" % SHOTS_FILE)
		return []
	return ((data as Dictionary).get("shots", []) as Array).filter(
		func(s: Dictionary) -> bool: return str(s.get("scene", "")) == SCENE_PATH)


func _on_moment(moment: String) -> void:
	if not _shot_of_moment.has(moment):
		return
	await get_tree().create_timer(SETTLE_BEFORE_SHOT_S).timeout
	await _capture(str(_shot_of_moment[moment]["id"]))


func _headless() -> bool:
	return DisplayServer.get_name() == "headless"


func _capture(shot_id: String) -> void:
	if _headless():
		print("[FirstSeasonCapture] headless, no image for %s" % shot_id)
		_captured.append(shot_id)
		return
	var done := [false]
	RenderingServer.frame_post_draw.connect(func() -> void: done[0] = true, CONNECT_ONE_SHOT)
	var waited := 0.0
	while not done[0] and waited < 3.0:
		await get_tree().create_timer(0.1).timeout
		waited += 0.1
	var img := get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path(OUT_DIR + shot_id + ".png")
	if img == null or img.save_png(path) != OK:
		push_error("[FirstSeasonCapture] could not save %s" % shot_id)
		_capture_failures += 1
		return
	_captured.append(shot_id)
	print("[FirstSeasonCapture] captured %s -> %s" % [shot_id, path])
