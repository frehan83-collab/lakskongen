extends "res://tests/UiFlowTests.gd"
## Scratch (never committed): one part of FirstSeasonFlowChecks alone.

func _ready() -> void:
	_go.call_deferred()

func _go() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	mgr.UseSaveDirectory(TEST_SAVE_DIR)
	CampaignProgress.path_override = TEST_ROOT + "campaign.json"
	CampaignProgress.reset()
	TranslationServer.set_locale("no")
	GameplayPrefs.force_tutorial(true)
	GameplayPrefs.force_auto_pause(true)
	MotionPrefs.force(true)
	var c: RefCounted = preload("res://tests/FirstSeasonFlowChecks.gd").new()
	c.t = self
	await c._a_player_using_only_input_wins_the_first_season()
	CampaignProgress.reset()
	_finish()
