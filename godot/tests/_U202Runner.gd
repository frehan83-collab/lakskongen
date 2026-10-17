extends "res://tests/UiFlowTests.gd"
## Dev-only runner (not committed): the U2-02 checks alone.

func _ready() -> void:
	_run_u202.call_deferred()

func _run_u202() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	mgr.UseSaveDirectory(TEST_SAVE_DIR)
	CampaignProgress.path_override = TEST_ROOT + "campaign.json"
	CampaignProgress.reset()
	TranslationServer.set_locale("no")
	await preload("res://tests/FirstSeasonFlowChecks.gd").new().run(self)
	CampaignProgress.reset()
	_finish()
